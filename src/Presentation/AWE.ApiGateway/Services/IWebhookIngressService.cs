using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace AWE.ApiGateway.Services;

public interface IWebhookIngressService
{
    Task<WebhookIngressResult> HandleCatchAsync(string routePath, JsonElement payload, IHeaderDictionary headers, CancellationToken cancellationToken = default);
}

public enum WebhookIngressStatus
{
    Accepted,
    Duplicate,
    RouteNotFound,
    Unauthorized,
    InvalidIdempotencyPath
}

public sealed record WebhookIngressResult(
    WebhookIngressStatus Status,
    string RoutePath,
    Guid? DefinitionId = null,
    Guid? CorrelationId = null,
    string? IdempotencyKey = null,
    string? Message = null
);
