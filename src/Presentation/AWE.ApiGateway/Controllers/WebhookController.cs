using System.Text.Json;
using AWE.ApiGateway.Services;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Infrastructure.Persistence;
using AWE.Shared.Consts;
using MassTransit;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebhookIngressService _webhookIngressService;

    public WebhookController(
        IExecutionPointerRepository pointerRepo,
        IWorkflowInstanceRepository instanceRepo,
        IUnitOfWork uow,
        IPublishEndpoint publishEndpoint,
        ApplicationDbContext context,
        IWebhookIngressService webhookIngressService)
    {
        _pointerRepo = pointerRepo;
        _instanceRepo = instanceRepo;
        _uow = uow;
        _publishEndpoint = publishEndpoint;
        _dbContext = context;
        _webhookIngressService = webhookIngressService;
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

        if (pointer.Status != ExecutionPointerStatus.Suspended)
            return BadRequest(new { error = "Pointer is not in Suspended state." });

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
    [EnableRateLimiting("WebhookIngress")]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> TriggerWorkflow(Guid definitionId, [FromBody] JsonElement payload)
    {
        var correlationId = Guid.NewGuid();
        var jobName = $"Webhook-Triggered-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

        var command = new SubmitWorkflowCommand(
            DefinitionId: definitionId,
            JobName: jobName,
            InputData: payload.GetRawText(),
            CorrelationId: correlationId,
            TriggerSource: WorkflowTriggerSource.Webhook
        );

        await _publishEndpoint.Publish(command);
        await _dbContext.SaveChangesAsync();

        return Accepted(new
        {
            message = "🎯 Webhook received. Workflow is starting...",
            correlationId,
            definitionId
        });
    }

    /// <summary>
    /// API để hệ thống bên ngoài (Github, Stripe...) gọi vào để Start Workflow theo route động
    /// </summary>
    [HttpPost("catch/{routePath}")]
    [EnableRateLimiting("WebhookIngress")]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> CatchWebhook(string routePath, [FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        var result = await _webhookIngressService.HandleCatchAsync(routePath, payload, Request.Headers, cancellationToken);

        return result.Status switch
        {
            WebhookIngressStatus.RouteNotFound => NotFound(new { error = result.Message }),
            WebhookIngressStatus.Unauthorized => Unauthorized(new { error = result.Message }),
            WebhookIngressStatus.InvalidIdempotencyPath => BadRequest(new { error = result.Message }),
            WebhookIngressStatus.Duplicate => Ok(new
            {
                message = result.Message,
                routePath = result.RoutePath,
                idempotencyKey = result.IdempotencyKey
            }),
            _ => Ok(new
            {
                message = result.Message,
                routePath = result.RoutePath,
                correlationId = result.CorrelationId,
                definitionId = result.DefinitionId,
                idempotencyKey = result.IdempotencyKey
            })
        };
    }
}
