using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;

namespace AWE.Application.UseCases.Workflows;

internal static class WebhookRouteSyncHelper
{
    public static async Task SyncAsync(
        IWebhookRouteRepository webhookRouteRepository,
        Guid workflowDefinitionId,
        JsonDocument definitionJson,
        CancellationToken cancellationToken)
    {
        var desiredRoutes = ExtractWebhookRoutes(definitionJson)
            .GroupBy(x => x.RoutePath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        var existingRoutes = await webhookRouteRepository.GetByWorkflowDefinitionIdAsync(workflowDefinitionId, cancellationToken);
        var desiredRoutePaths = desiredRoutes
            .Select(x => x.RoutePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var existing in existingRoutes)
        {
            if (!desiredRoutePaths.Contains(existing.RoutePath))
            {
                existing.Deactivate();
                await webhookRouteRepository.UpdateAsync(existing, cancellationToken);
            }
        }

        foreach (var route in desiredRoutes)
        {
            var existing = await webhookRouteRepository.GetByRoutePathAsync(route.RoutePath, cancellationToken);
            if (existing is null)
            {
                await webhookRouteRepository.AddAsync(new WebhookRoute(
                    route.RoutePath,
                    workflowDefinitionId,
                    route.SecretToken,
                    route.IdempotencyKeyPath), cancellationToken);

                continue;
            }

            existing.UpdateRoute(workflowDefinitionId, route.SecretToken, route.IdempotencyKeyPath);
            await webhookRouteRepository.UpdateAsync(existing, cancellationToken);
        }
    }

    private static IReadOnlyList<WebhookRouteConfig> ExtractWebhookRoutes(JsonDocument definitionJson)
    {
        var result = new List<WebhookRouteConfig>();

        if (!definitionJson.RootElement.TryGetProperty("Steps", out var steps)
            || steps.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var step in steps.EnumerateArray())
        {
            if (!TryGetStringProperty(step, "Type", out var type)
                || !string.Equals(type, "WebhookTrigger", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryGetPropertyIgnoreCase(step, "Inputs", out var inputs)
                || inputs.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryGetStringProperty(inputs, "RoutePath", out var routePath)
                || string.IsNullOrWhiteSpace(routePath))
            {
                continue;
            }

            TryGetStringProperty(inputs, "SecretToken", out var secretToken);
            TryGetStringProperty(inputs, "IdempotencyKeyPath", out var idempotencyKeyPath);

            result.Add(new WebhookRouteConfig(
                routePath.Trim(),
                string.IsNullOrWhiteSpace(secretToken) ? null : secretToken.Trim(),
                string.IsNullOrWhiteSpace(idempotencyKeyPath) ? null : idempotencyKeyPath.Trim()));
        }

        return result;
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
    {
        value = null;

        if (!TryGetPropertyIgnoreCase(element, propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement propertyValue)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    propertyValue = property.Value;
                    return true;
                }
            }
        }

        propertyValue = default;
        return false;
    }

    private sealed record WebhookRouteConfig(string RoutePath, string? SecretToken, string? IdempotencyKeyPath);
}
