using System.Text.Json;
using AWE.Domain.Enums;
using AWE.WorkflowEngine.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AWE.WorkflowEngine.Tests;

public class TransitionEvaluatorTests
{
    private readonly TransitionEvaluator _sut = new(
        new VariableResolver(),
        NullLogger<TransitionEvaluator>.Instance);

    [Fact]
    public void FindStartNodeIdsByTrigger_WhenManualTriggerExists_ReturnsOnlyManualTriggerNodes()
    {
        using var definition = JsonDocument.Parse("""
        {
          "Steps": [
            { "Id": "manual", "Type": "ManualTrigger" },
            { "Id": "webhook", "Type": "WebhookTrigger", "Inputs": { "RoutePath": "/github" } },
            { "Id": "log", "Type": "Log" }
          ],
          "Transitions": [
            { "Id": "t1", "Source": "manual", "Target": "log" },
            { "Id": "t2", "Source": "webhook", "Target": "log" }
          ]
        }
        """);

        var starts = _sut.FindStartNodeIdsByTrigger(definition, WorkflowTriggerSource.Manual);

        var start = Assert.Single(starts);
        Assert.Equal("manual", start);
    }

    [Fact]
    public void EvaluateTransitions_EvaluatesTrueFalseAndMissingVariableAsFalse()
    {
        using var definition = JsonDocument.Parse("""
        {
          "Steps": [
            { "Id": "manual", "Type": "ManualTrigger" },
            { "Id": "high", "Type": "Log" },
            { "Id": "low", "Type": "Log" },
            { "Id": "missing", "Type": "Log" }
          ],
          "Transitions": [
            { "Id": "t1", "Source": "manual", "Target": "high", "Condition": "{{workflow.input.score}} >= 80" },
            { "Id": "t2", "Source": "manual", "Target": "low", "Condition": "{{workflow.input.score}} < 80" },
            { "Id": "t3", "Source": "manual", "Target": "missing", "Condition": "{{workflow.input.unknown}} == 1" }
          ]
        }
        """);
        using var context = JsonDocument.Parse("""
        {
          "Inputs": { "score": 90 },
          "Steps": {},
          "Meta": {}
        }
        """);

        var transitions = _sut.EvaluateTransitions(definition, "manual", context);

        Assert.Equal(3, transitions.Count);
        Assert.Contains(transitions, x => x.TargetNodeId == "high" && x.IsConditionMet);
        Assert.Contains(transitions, x => x.TargetNodeId == "low" && !x.IsConditionMet);
        Assert.Contains(transitions, x => x.TargetNodeId == "missing" && !x.IsConditionMet);
    }

    [Fact]
    public void GetIncomingEdgesCount_CountsAllTransitionsTargetingJoinNode()
    {
        using var definition = JsonDocument.Parse("""
        {
          "Steps": [
            { "Id": "a", "Type": "Log" },
            { "Id": "b", "Type": "Log" },
            { "Id": "join", "Type": "Join" }
          ],
          "Transitions": [
            { "Id": "t1", "Source": "a", "Target": "join" },
            { "Id": "t2", "Source": "b", "Target": "join" },
            { "Id": "t3", "Source": "a", "Target": "b" }
          ]
        }
        """);

        var count = _sut.GetIncomingEdgesCount(definition, "join");

        Assert.Equal(2, count);
        Assert.True(_sut.IsJoinNode(definition, "join"));
        Assert.False(_sut.IsJoinNode(definition, "a"));
    }
}
