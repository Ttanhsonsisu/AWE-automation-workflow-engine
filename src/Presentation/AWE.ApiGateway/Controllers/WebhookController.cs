using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Enums;
using AWE.Infrastructure.Persistence;
using AWE.Shared.Consts;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AWE.ApiGateway.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhookController : ControllerBase
{
    private readonly IExecutionPointerRepository _pointerRepo;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IUnitOfWork _uow;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ApplicationDbContext dbcontext;
    public WebhookController(
        IExecutionPointerRepository pointerRepo,
        IWorkflowInstanceRepository instanceRepo,
        IUnitOfWork uow,
        IPublishEndpoint publishEndpoint,
        ApplicationDbContext context)
    {
        _pointerRepo = pointerRepo;
        _instanceRepo = instanceRepo;
        _uow = uow;
        _publishEndpoint = publishEndpoint;
        dbcontext = context;
    }

    /// <summary>
    /// API đánh thức một Node đang ngủ (Wait)
    /// </summary>
    [HttpPost("resume/{pointerId}")]
    public async Task<IActionResult> ResumeWorkflow(Guid pointerId, [FromBody] JsonElement payload)
    {
        // 1. Tìm Pointer đang ngủ
        var pointer = await _pointerRepo.GetPointerByIdAsync(pointerId);
        if (pointer == null)
            return NotFound(new { error = "Pointer not found." });

        if (pointer.Status != ExecutionPointerStatus.WaitingForEvent)
            return BadRequest(new { error = "Pointer is not in Waiting state." });

        var instance = await _instanceRepo.GetInstanceByIdAsync(pointer.InstanceId);

        // 2. Ép kiểu payload thành JsonDocument để lưu DB
        var payloadDoc = JsonDocument.Parse(payload.GetRawText());

        // 3. Cập nhật trạng thái thành Completed (Bypass Worker)
        pointer.CompleteFromWait(payloadDoc);

        // Nhờ có Transactional Outbox, lệnh Save DB và Publish Event này là Atomic!
        

        // 4. Giả lập Worker, bắn StepCompletedEvent cho Engine chạy tiếp Next Node
        var routingKey = $"{MessagingConstants.PatternEvent.TrimEnd('#')}completed";

        await _publishEndpoint.Publish(new StepCompletedEvent(
            WorkflowInstanceId: instance!.Id,
            ExecutionPointerId: pointer.Id,
            StepId: pointer.StepId,
            Output: payloadDoc,
            CompletedAt: DateTime.UtcNow
        ), context =>
        {
            context.SetRoutingKey(routingKey);
        });

        await _uow.SaveChangesAsync();

        return Ok(new { message = "🚀 Workflow resumed successfully!" });
    }

    /// <summary>
    /// API để hệ thống bên ngoài (Github, Stripe...) gọi vào để Start Workflow
    /// </summary>
    [HttpPost("trigger/{definitionId}")]
    public async Task<IActionResult> TriggerWorkflow(Guid definitionId, [FromBody] JsonElement payload)
    {
        // 1. Kiểm tra Definition có tồn tại không (Tuỳ chọn, để bảo mật tốt hơn)
        // var def = await _defRepo.GetDefinitionByIdAsync(definitionId);
        // if (def == null) return NotFound("Workflow Definition not found");

        // 2. Tạo Command gửi cho Engine
        var correlationId = Guid.NewGuid();
        var jobName = $"Webhook-Triggered-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

        var command = new SubmitWorkflowCommand(
            DefinitionId: definitionId,
            JobName: jobName,
            InputData: payload.GetRawText(),
            CorrelationId: correlationId
        );

        // 3. Publish Command vào Message Broker
        // (Worker.Engine có JobExecutionConsumer đang lắng nghe cái này)
        await _publishEndpoint.Publish(command);

        await dbcontext.SaveChangesAsync();

        // LƯU Ý: Nếu Controller này có gọi DB thay đổi gì thì mới cần await _uow.SaveChangesAsync();
        // Ở đây ta chỉ đẩy message thẳng vào Queue (nếu MassTransit cấu hình Outbox cho toàn Bus thì message vẫn an toàn)

        // 4. Trả về 202 Accepted (Đã tiếp nhận yêu cầu)
        return Accepted(new
        {
            message = "🎯 Webhook received. Workflow is starting...",
            correlationId = correlationId,
            definitionId = definitionId
        });
    }
}
