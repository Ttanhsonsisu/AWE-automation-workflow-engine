using System.Text.Json;

namespace AWE.WorkflowEngine.Interfaces;

public interface ITransitionEvaluator
{
    string FindStartNodeId(JsonDocument defJson);
    List<(string TargetNodeId, bool IsConditionMet)> EvaluateTransitions(JsonDocument defJson, string currentId, JsonDocument context);
    bool IsJoinNode(JsonDocument defJson, string stepId);
    int GetIncomingEdgesCount(JsonDocument defJson, string stepId);
}
