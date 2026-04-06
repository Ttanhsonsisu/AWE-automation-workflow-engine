using System.Text.Json;

namespace AWE.Application.Abstractions.CoreEngine;

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

    /// <summary>
    /// Finds the identifiers of all start nodes of a specific type within the provided JSON document definition.
    /// </summary>
    /// <param name="defJson">The JSON document that defines the node structure to search. Cannot be null.</param>
    /// <returns>A list of strings containing the identifiers of start nodes matching the required type. The list is empty if no
    /// matching nodes are found.</returns>
    List<string> FindStartNodeIdsWithType(JsonDocument defJson);
}
