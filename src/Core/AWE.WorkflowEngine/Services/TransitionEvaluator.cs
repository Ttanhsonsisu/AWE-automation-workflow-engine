using System.Text.Json;
using AWE.Application.Abstractions.CoreEngine;
using Jint;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Services;

public class TransitionEvaluator(IVariableResolver resolver, ILogger<TransitionEvaluator> logger) : ITransitionEvaluator
{
    private readonly IVariableResolver _resolver = resolver;
    private readonly ILogger<TransitionEvaluator> _logger = logger;

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

    public List<string> FindStartNodeIdsWithType(JsonDocument defJson)
    {
        var root = defJson.RootElement;
        if (!root.TryGetProperty("Steps", out var stepsElement))
            throw new InvalidOperationException("Workflow definition is missing 'Steps' array.");

        var startNodes = new List<string>();

        // 1. Khai báo danh sách các loại Plugin được cấp quyền làm Start Node (Trigger)
        // Dùng HashSet + StringComparer.OrdinalIgnoreCase để check siêu nhanh và không phân biệt hoa thường
        var allowedTriggerTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ManualTrigger",
            "WebhookTrigger"
            //"WebhookReceiver",
            //"ScheduleTimer" 
        };

        // 2. Quét toàn bộ danh sách Node
        foreach (var step in stepsElement.EnumerateArray())
        {
            var id = step.GetProperty("Id").GetString()!;

            // Kiểm tra trường "Type" (Hoặc "PluginType" tùy theo cách bạn đặt tên trong JSON)
            if (step.TryGetProperty("Type", out var typeElement))
            {
                var pluginType = typeElement.GetString();

                // Nếu Node này thuộc nhóm Trigger -> Thêm nó vào danh sách Start Nodes!
                if (!string.IsNullOrEmpty(pluginType) && allowedTriggerTypes.Contains(pluginType))
                {
                    startNodes.Add(id);
                }
            }
        }

        return startNodes;
    }

    private bool EvaluateCondition(string conditionExpression, JsonDocument context)
    {
        if (string.IsNullOrWhiteSpace(conditionExpression)) return true;

        try
        {
            var resolveResult = _resolver.Resolve(conditionExpression, context);

            if (!resolveResult.IsSuccess)
            {
                // Nếu thiếu biến trong câu điều kiện (VD: {{Steps.Node1.Output.Score}} > 5 nhưng Node1 không trả về Score)
                // Ta log lại lỗi và trả về false (ngắt luồng rẽ nhánh này - Dead path)
                _logger.LogWarning("⚠️ Condition Evaluation aborted. Missing variables: {Errors}. Expression: {Exp}",
                    resolveResult.ErrorMessage, conditionExpression);
                return false;
            }

            string resolvedExpression = resolveResult.ResolvedPayload;

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
