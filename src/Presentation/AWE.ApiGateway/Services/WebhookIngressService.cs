using System.Text.Json;
using AWE.Contracts.Messages;
using AWE.Domain.Enums;
using AWE.Infrastructure.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AWE.ApiGateway.Services;

public class WebhookIngressService(
    ApplicationDbContext dbContext,
    IPublishEndpoint publishEndpoint,
    IWebhookSignatureVerifier signatureVerifier) : IWebhookIngressService
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IPublishEndpoint _publishEndpoint = publishEndpoint;
    private readonly IWebhookSignatureVerifier _signatureVerifier = signatureVerifier;

    public async Task<WebhookIngressResult> HandleCatchAsync(
        string routePath,
        JsonElement payload,
        IHeaderDictionary headers,
        CancellationToken cancellationToken = default)
    {
        var route = await _dbContext.WebhookRoutes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RoutePath == routePath && x.IsActive, cancellationToken);

        if (route is null)
        {
            return new WebhookIngressResult(
                WebhookIngressStatus.RouteNotFound,
                routePath,
                Message: "Webhook route not found or inactive.");
        }

        var isSignatureValid = _signatureVerifier.Verify(route.SecretToken, headers, payload.GetRawText());
        if (!isSignatureValid)
        {
            return new WebhookIngressResult(
                WebhookIngressStatus.Unauthorized,
                routePath,
                DefinitionId: route.WorkflowDefinitionId,
                Message: "Invalid webhook signature.");
        }

        var extraction = ExtractIdempotencyKey(route.IdempotencyKeyPath, payload, headers);
        if (!extraction.IsValid)
        {
            return new WebhookIngressResult(
                WebhookIngressStatus.InvalidIdempotencyPath,
                routePath,
                DefinitionId: route.WorkflowDefinitionId,
                Message: extraction.ErrorMessage);
        }

        var idempotencyKey = extraction.Value;
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var exists = await _dbContext.WorkflowInstances
                .AsNoTracking()
                .AnyAsync(x => x.DefinitionId == route.WorkflowDefinitionId && x.IdempotencyKey == idempotencyKey, cancellationToken);

            if (exists)
            {
                return new WebhookIngressResult(
                    WebhookIngressStatus.Duplicate,
                    routePath,
                    DefinitionId: route.WorkflowDefinitionId,
                    IdempotencyKey: idempotencyKey,
                    Message: "Duplicate webhook ignored.");
            }
        }

        var correlationId = Guid.NewGuid();
        var command = new SubmitWorkflowCommand(
            DefinitionId: route.WorkflowDefinitionId,
            JobName: $"Webhook-Triggered-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            InputData: payload.GetRawText(),
            CorrelationId: correlationId,
            IdempotencyKey: idempotencyKey,
            TriggerSource: WorkflowTriggerSource.Webhook,
            TriggerRoutePath: routePath
        );

        await _publishEndpoint.Publish(command, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new WebhookIngressResult(
            WebhookIngressStatus.Accepted,
            routePath,
            DefinitionId: route.WorkflowDefinitionId,
            CorrelationId: correlationId,
            IdempotencyKey: idempotencyKey,
            Message: "Webhook received.");
    }

    private static IdempotencyKeyExtractionResult ExtractIdempotencyKey(string? idempotencyKeyPath, JsonElement payload, IHeaderDictionary headers)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKeyPath))
        {
            return IdempotencyKeyExtractionResult.Success(null);
        }

        var normalizedPath = idempotencyKeyPath.Trim();
        if (normalizedPath.StartsWith("$.", StringComparison.Ordinal))
        {
            normalizedPath = normalizedPath[2..];
        }

        if (normalizedPath.StartsWith("header.", StringComparison.OrdinalIgnoreCase))
        {
            var headerName = normalizedPath["header.".Length..];
            if (string.IsNullOrWhiteSpace(headerName))
            {
                return IdempotencyKeyExtractionResult.Failure("IdempotencyKeyPath không hợp lệ: thiếu tên header.");
            }

            if (headers.TryGetValue(headerName, out var headerValue))
            {
                return IdempotencyKeyExtractionResult.Success(string.IsNullOrWhiteSpace(headerValue) ? null : headerValue.ToString());
            }

            return IdempotencyKeyExtractionResult.Success(null);
        }

        if (normalizedPath.StartsWith("body.", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath["body.".Length..];
        }

        if (string.IsNullOrWhiteSpace(normalizedPath)
            || normalizedPath.Contains("..", StringComparison.Ordinal)
            || normalizedPath.StartsWith(".", StringComparison.Ordinal)
            || normalizedPath.EndsWith(".", StringComparison.Ordinal))
        {
            return IdempotencyKeyExtractionResult.Failure("IdempotencyKeyPath không hợp lệ cho body path.");
        }

        if (TryGetJsonValueByPath(payload, normalizedPath, out var element))
        {
            var value = element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.GetRawText();

            return IdempotencyKeyExtractionResult.Success(value);
        }

        return IdempotencyKeyExtractionResult.Success(null);
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

    private readonly record struct IdempotencyKeyExtractionResult(bool IsValid, string? Value, string? ErrorMessage)
    {
        public static IdempotencyKeyExtractionResult Success(string? value) => new(true, value, null);
        public static IdempotencyKeyExtractionResult Failure(string errorMessage) => new(false, null, errorMessage);
    }
}
