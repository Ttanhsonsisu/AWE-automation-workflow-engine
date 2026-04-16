using AWE.Application.Abstractions.Persistence;
using AWE.Application.UseCases.Workflows;
using AWE.Shared.Primitives;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AWE.Application.UseCases.Workflows.PublishDefinition;

public class PublishDefinitionUseCase : IPublishDefinitionUseCase
{
    private static readonly Regex RoutePathPattern = new(
        @"^[a-zA-Z0-9](?:[a-zA-Z0-9_/-]*[a-zA-Z0-9])?$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    private static readonly HashSet<string> TriggerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ManualTrigger",
        "WebhookTrigger",
        "CronTrigger",
        "ChatTrigger"
    };

    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWebhookRouteRepository _webhookRouteRepository;
    private readonly IUnitOfWork _unitOfWork;

    public PublishDefinitionUseCase(
        IWorkflowDefinitionRepository definitionRepository,
        IWebhookRouteRepository webhookRouteRepository,
        IUnitOfWork unitOfWork)
    {
        _definitionRepository = definitionRepository;
        _webhookRouteRepository = webhookRouteRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PublishDefinitionResponse>> ExecuteAsync(PublishDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var definition = await _definitionRepository.GetDefinitionByIdAsync(request.Id, cancellationToken);
        if (definition is null)
        {
            return Result.Failure<PublishDefinitionResponse>(
                Error.NotFound("PublishDefinition.NotFound", "Workflow definition not found"));
        }

        var routeValidationError = ValidateWebhookRouteConfiguration(definition.DefinitionJson);
        if (routeValidationError is not null)
        {
            return Result.Failure<PublishDefinitionResponse>(routeValidationError);
        }

        var topologyValidationError = ValidateJoinTriggerIsolation(definition.DefinitionJson);
        if (topologyValidationError is not null)
        {
            return Result.Failure<PublishDefinitionResponse>(topologyValidationError);
        }

        if (!definition.IsPublished)
        {
            definition.Publish();
            await _definitionRepository.UpdateDefinitionAsync(definition, cancellationToken);
        }

        await WebhookRouteSyncHelper.SyncAsync(_webhookRouteRepository, definition.Id, definition.DefinitionJson, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PublishDefinitionResponse
        {
            Id = definition.Id,
            Name = definition.Name,
            Version = definition.Version,
            IsPublished = definition.IsPublished
        });
    }

    private static Error? ValidateJoinTriggerIsolation(JsonDocument definitionJson)
    {
        if (!definitionJson.RootElement.TryGetProperty("Steps", out var steps)
            || steps.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var stepTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var joinStepIds = new List<string>();

        foreach (var step in steps.EnumerateArray())
        {
            if (!step.TryGetProperty("Id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var stepId = idElement.GetString();
            if (string.IsNullOrWhiteSpace(stepId))
            {
                continue;
            }

            var stepType = step.TryGetProperty("Type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString() ?? string.Empty
                : string.Empty;

            stepTypes[stepId] = stepType;
            if (string.Equals(stepType, "Join", StringComparison.OrdinalIgnoreCase))
            {
                joinStepIds.Add(stepId);
            }
        }

        if (joinStepIds.Count == 0)
        {
            return null;
        }

        var incomingByTarget = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (definitionJson.RootElement.TryGetProperty("Transitions", out var transitions)
            && transitions.ValueKind == JsonValueKind.Array)
        {
            foreach (var transition in transitions.EnumerateArray())
            {
                if (!transition.TryGetProperty("Source", out var sourceElement)
                    || sourceElement.ValueKind != JsonValueKind.String
                    || !transition.TryGetProperty("Target", out var targetElement)
                    || targetElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var source = sourceElement.GetString();
                var target = targetElement.GetString();
                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                if (!incomingByTarget.TryGetValue(target, out var sources))
                {
                    sources = [];
                    incomingByTarget[target] = sources;
                }

                sources.Add(source);
            }
        }

        foreach (var joinStepId in joinStepIds)
        {
            var upstreamTriggerTypes = FindUpstreamTriggerTypes(joinStepId, incomingByTarget, stepTypes);
            if (upstreamTriggerTypes.Count > 1)
            {
                var triggerSummary = string.Join(", ", upstreamTriggerTypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                return Error.Validation(
                    "Workflow.Definition.InvalidTriggerJoin",
                    $"Join node '{joinStepId}' nhận nhánh từ nhiều trigger khác loại ({triggerSummary}). Hãy tách workflow hoặc dùng chiến lược merge phù hợp.");
            }
        }

        return null;
    }

    private static HashSet<string> FindUpstreamTriggerTypes(
        string joinStepId,
        IReadOnlyDictionary<string, List<string>> incomingByTarget,
        IReadOnlyDictionary<string, string> stepTypes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(joinStepId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (stepTypes.TryGetValue(current, out var stepType)
                && TriggerTypes.Contains(stepType))
            {
                result.Add(stepType);
            }

            if (!incomingByTarget.TryGetValue(current, out var incomingSources))
            {
                continue;
            }

            foreach (var source in incomingSources)
            {
                stack.Push(source);
            }
        }

        return result;
    }

    private static Error? ValidateWebhookRouteConfiguration(JsonDocument definitionJson)
    {
        if (!definitionJson.RootElement.TryGetProperty("Steps", out var steps)
            || steps.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var seenRoutePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps.EnumerateArray())
        {
            if (!TryGetStringProperty(step, "Type", out var stepType)
                || !string.Equals(stepType, "WebhookTrigger", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var stepId = TryGetStringProperty(step, "Id", out var parsedStepId)
                ? parsedStepId ?? "(unknown)"
                : "(unknown)";

            if (!TryGetPropertyIgnoreCase(step, "Inputs", out var inputs)
                || inputs.ValueKind != JsonValueKind.Object)
            {
                return Error.Validation(
                    "Workflow.WebhookRoute.InputsRequired",
                    $"WebhookTrigger '{stepId}' thiếu object Inputs.");
            }

            if (!TryGetStringProperty(inputs, "RoutePath", out var routePath)
                || string.IsNullOrWhiteSpace(routePath))
            {
                return Error.Validation(
                    "Workflow.WebhookRoute.RoutePathRequired",
                    $"WebhookTrigger '{stepId}' phải có RoutePath hợp lệ.");
            }

            var normalizedRoutePath = routePath.Trim();
            if (!RoutePathPattern.IsMatch(normalizedRoutePath))
            {
                return Error.Validation(
                    "Workflow.WebhookRoute.RoutePathInvalid",
                    $"RoutePath '{normalizedRoutePath}' của WebhookTrigger '{stepId}' không hợp lệ.");
            }

            if (!seenRoutePaths.Add(normalizedRoutePath))
            {
                return Error.Validation(
                    "Workflow.WebhookRoute.RoutePathDuplicate",
                    $"RoutePath '{normalizedRoutePath}' bị trùng trong workflow definition.");
            }
        }

        return null;
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
}
