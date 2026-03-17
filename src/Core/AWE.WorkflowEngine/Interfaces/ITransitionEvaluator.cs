using System.Text.Json;

namespace AWE.WorkflowEngine.Interfaces;

public interface ITransitionEvaluator
{
    string FindStartNodeId(JsonDocument defJson);
    List<(string TargetNodeId, bool IsConditionMet)> EvaluateTransitions(JsonDocument defJson, string currentId, JsonDocument context);
    bool IsJoinNode(JsonDocument defJson, string stepId);
    int GetIncomingEdgesCount(JsonDocument defJson, string stepId);
    /// <summary>
    /// support multiple start nodes, return all start node ids. if no start node, return empty list.
    /// </summary>
    /// <param name="defJson"></param>
    /// <returns></returns>
    List<string> FindStartNodeIds(JsonDocument defJson);
}
