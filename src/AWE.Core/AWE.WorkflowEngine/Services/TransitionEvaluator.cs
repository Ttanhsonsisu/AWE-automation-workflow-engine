using System.Text.Json;
using AWE.WorkflowEngine.Interfaces;
using Jint;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Services;

public class TransitionEvaluator : ITransitionEvaluator
{
    private readonly IVariableResolver _resolver;
    private readonly ILogger<TransitionEvaluator> _logger;

    public TransitionEvaluator(IVariableResolver resolver, ILogger<TransitionEvaluator> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    public string FindStartNodeId(JsonDocument defJson)
    {
        var root = defJson.RootElement;
        var steps = root.GetProperty("Steps").EnumerateArray();

        var targetNodes = new HashSet<string>();
        if (root.TryGetProperty("Transitions", out var transitions))
        {
            foreach (var t in transitions.EnumerateArray())
                targetNodes.Add(t.GetProperty("Target").GetString()!);
        }

        // Start node là node không nằm trong bất kỳ Target nào
        foreach (var step in steps)
        {
            var id = step.GetProperty("Id").GetString()!;
            if (!targetNodes.Contains(id)) return id;
        }

        throw new InvalidOperationException("Cannot find a valid start node (no node without incoming edges).");
    }

    public List<(string TargetNodeId, bool IsConditionMet)> EvaluateTransitions(JsonDocument defJson, string currentId, JsonDocument context)
    {
        var nextTransitions = new List<(string, bool)>();
        if (defJson.RootElement.TryGetProperty("Transitions", out var transArray))
        {
            foreach (var t in transArray.EnumerateArray())
            {
                if (t.GetProperty("Source").GetString() == currentId)
                {
                    string target = t.GetProperty("Target").GetString()!;
                    bool conditionMet = true;

                    if (t.TryGetProperty("Condition", out var conditionElem))
                    {
                        conditionMet = EvaluateCondition(conditionElem.GetString() ?? "", context);
                    }
                    nextTransitions.Add((target, conditionMet));
                }
            }
        }
        return nextTransitions;
    }

    public bool IsJoinNode(JsonDocument defJson, string stepId)
    {
        if (defJson.RootElement.TryGetProperty("Steps", out var steps))
        {
            foreach (var step in steps.EnumerateArray())
            {
                if (step.GetProperty("Id").GetString() == stepId)
                {
                    return step.TryGetProperty("Type", out var type) && type.GetString() == "Join";
                }
            }
        }
        return false;
    }

    public int GetIncomingEdgesCount(JsonDocument defJson, string stepId)
    {
        int count = 0;
        if (defJson.RootElement.TryGetProperty("Transitions", out var transitions))
        {
            foreach (var t in transitions.EnumerateArray())
            {
                if (t.TryGetProperty("Target", out var target) && target.GetString() == stepId)
                {
                    count++;
                }
            }
        }
        return count;
    }

    public List<string> FindStartNodeIds(JsonDocument defJson)
    {
        var root = defJson.RootElement;
        if (!root.TryGetProperty("Steps", out var stepsElement))
            throw new InvalidOperationException("Workflow definition is missing 'Steps' array.");

        var steps = stepsElement.EnumerateArray();
        var targetNodes = new HashSet<string>();

        if (root.TryGetProperty("Transitions", out var transitions))
        {
            foreach (var t in transitions.EnumerateArray())
            {
                if (t.TryGetProperty("Target", out var target))
                    targetNodes.Add(target.GetString()!);
            }
        }

        var startNodes = new List<string>();

        // Gom TẤT CẢ các node không có đầu vào
        foreach (var step in steps)
        {
            var id = step.GetProperty("Id").GetString()!;
            if (!targetNodes.Contains(id))
            {
                startNodes.Add(id);
            }
        }

        if (startNodes.Count == 0)
            throw new InvalidOperationException("Cannot find any valid start nodes.");

        return startNodes;
    }

    private bool EvaluateCondition(string conditionExpression, JsonDocument context)
    {
        if (string.IsNullOrWhiteSpace(conditionExpression)) return true;

        try
        {
            string resolvedExpression = _resolver.Resolve(conditionExpression, context);

            // [FIX] Cấu hình Jint Sandbox: Timeout 2s, Limit RAM 4MB để chống hack/crash
            var engine = new Engine(options =>
            {
                options.TimeoutInterval(TimeSpan.FromSeconds(2));
                options.LimitMemory(4_000_000);
            });

            return engine.Evaluate(resolvedExpression).AsBoolean();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ Condition Evaluation Failed. Expression: {Exp}", conditionExpression);
            return false; // Fail-safe
        }
    }
}
