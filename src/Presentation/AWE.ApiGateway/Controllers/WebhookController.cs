using System.Security.Cryptography;
using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
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
    private const string SignatureHeaderName = "X-Signature";

    private readonly IExecutionPointerRepository _pointerRepo;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IUnitOfWork _uow;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ApplicationDbContext _dbContext;

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
        _dbContext = context;
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
    public async Task<IActionResult> TriggerWorkflow(Guid definitionId, [FromBody] JsonElement payload)
    {
        var correlationId = Guid.NewGuid();
        var jobName = $"Webhook-Triggered-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

        var command = new SubmitWorkflowCommand(
            DefinitionId: definitionId,
            JobName: jobName,
            InputData: payload.GetRawText(),
            CorrelationId: correlationId
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
    public async Task<IActionResult> CatchWebhook(string routePath, [FromBody] JsonElement payload)
    {
        var route = await _dbContext.WebhookRoutes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RoutePath == routePath && x.IsActive);

        if (route is null)
        {
            return NotFound(new { error = "Webhook route not found or inactive." });
        }

        if (!VerifySignature(route.SecretToken))
        {
            return Unauthorized(new { error = "Invalid webhook signature." });
        }

        var idempotencyKey = ExtractIdempotencyKey(route.IdempotencyKeyPath, payload);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var exists = await _dbContext.WorkflowInstances
                .AsNoTracking()
                .AnyAsync(x => x.DefinitionId == route.WorkflowDefinitionId && x.IdempotencyKey == idempotencyKey);

            if (exists)
            {
                return Ok(new
                {
                    message = "Duplicate webhook ignored.",
                    routePath,
                    idempotencyKey
                });
            }
        }

        var correlationId = Guid.NewGuid();
        var jobName = $"Webhook-Triggered-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

        var command = new SubmitWorkflowCommand(
            DefinitionId: route.WorkflowDefinitionId,
            JobName: jobName,
            InputData: payload.GetRawText(),
            CorrelationId: correlationId,
            IdempotencyKey: idempotencyKey
        );

        await _publishEndpoint.Publish(command);

        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = "Webhook received.",
            routePath,
            correlationId = correlationId,
            definitionId = route.WorkflowDefinitionId,
            idempotencyKey
        });
    }

    private bool VerifySignature(string? secretToken)
    {
        if (string.IsNullOrWhiteSpace(secretToken))
        {
            return true;
        }

        if (!Request.Headers.TryGetValue(SignatureHeaderName, out var providedSignature))
        {
            return false;
        }

        // chống timing attack bằng cách so sánh hmac trong thời gian cố định
        var tokenBytes = System.Text.Encoding.UTF8.GetBytes(secretToken);
        var signatureBytes = System.Text.Encoding.UTF8.GetBytes(providedSignature.ToString());

        return CryptographicOperations.FixedTimeEquals(tokenBytes, signatureBytes);
    }

    private string? ExtractIdempotencyKey(string? idempotencyKeyPath, JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKeyPath))
        {
            return null;
        }

        var normalizedPath = idempotencyKeyPath.Trim();
        if (normalizedPath.StartsWith("$.", StringComparison.Ordinal))
        {
            normalizedPath = normalizedPath[2..];
        }

        if (normalizedPath.StartsWith("header.", StringComparison.OrdinalIgnoreCase))
        {
            var headerName = normalizedPath["header.".Length..];
            if (Request.Headers.TryGetValue(headerName, out var headerValue))
            {
                return string.IsNullOrWhiteSpace(headerValue) ? null : headerValue.ToString();
            }

            return null;
        }

        if (normalizedPath.StartsWith("body.", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath["body.".Length..];
        }

        if (TryGetJsonValueByPath(payload, normalizedPath, out var element))
        {
            return element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.GetRawText();
        }

        return null;
    }

    private static bool TryGetJsonValueByPath(JsonElement payload, string path, out JsonElement element)
    {
        element = payload;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in segments)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
            {
                return false;
            }
        }

        return true;
    }
}
