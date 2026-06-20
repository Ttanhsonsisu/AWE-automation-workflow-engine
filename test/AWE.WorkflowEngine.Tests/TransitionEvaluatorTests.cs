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
    public void FindStartNodeIdsByTrigger_WhenWebhookRouteIsProvided_ReturnsOnlyMatchingWebhookTrigger()
    {
        using var definition = JsonDocument.Parse("""
        {
          "Steps": [
            { "Id": "github", "Type": "WebhookTrigger", "Inputs": { "RoutePath": "/github" } },
            { "Id": "stripe", "Type": "WebhookTrigger", "Inputs": { "RoutePath": "/stripe" } },
            { "Id": "manual", "Type": "ManualTrigger" }
          ],
          "Transitions": []
        }
        """);

        var starts = _sut.FindStartNodeIdsByTrigger(definition, WorkflowTriggerSource.Webhook, "/github");

        var start = Assert.Single(starts);
        Assert.Equal("github", start);
    }

    [Fact]
    public void FindStartNodeIdsByTrigger_WhenCronStepIdIsProvided_ReturnsOnlyMatchingCronTrigger()
    {
        using var definition = JsonDocument.Parse("""
        {
          "Steps": [
            { "Id": "nightly", "Type": "CronTrigger" },
            { "Id": "hourly", "Type": "CronTrigger" },
            { "Id": "manual", "Type": "ManualTrigger" }
          ],
          "Transitions": []
        }
        """);

        var starts = _sut.FindStartNodeIdsByTrigger(definition, WorkflowTriggerSource.Cron, "hourly");

        var start = Assert.Single(starts);
        Assert.Equal("hourly", start);
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
    public void EvaluateTransitions_WhenTransitionHasNoCondition_DefaultsToTrue()
    {
        using var definition = JsonDocument.Parse("""
        {
          "Steps": [
            { "Id": "manual", "Type": "ManualTrigger" },
            { "Id": "log", "Type": "Log" }
          ],
          "Transitions": [
            { "Id": "t1", "Source": "manual", "Target": "log" }
          ]
        }
        """);
        using var context = JsonDocument.Parse("""{"Inputs":{},"Steps":{},"Meta":{}}""");

        var transition = Assert.Single(_sut.EvaluateTransitions(definition, "manual", context));

        Assert.Equal("log", transition.TargetNodeId);
        Assert.True(transition.IsConditionMet);
    }

    [Fact]
    public void EvaluateTransitions_WhenConditionExpressionIsInvalid_ReturnsFalseFailSafe()
    {
        using var definition = JsonDocument.Parse("""
        {
          "Steps": [
            { "Id": "manual", "Type": "ManualTrigger" },
            { "Id": "bad", "Type": "Log" }
          ],
          "Transitions": [
            { "Id": "t1", "Source": "manual", "Target": "bad", "Condition": "(()" }
          ]
        }
        """);
        using var context = JsonDocument.Parse("""{"Inputs":{},"Steps":{},"Meta":{}}""");

        var transition = Assert.Single(_sut.EvaluateTransitions(definition, "manual", context));

        Assert.Equal("bad", transition.TargetNodeId);
        Assert.False(transition.IsConditionMet);
    }

    [Theory]
    [InlineData(true, "true-target", "false-target")]
    [InlineData(false, "false-target", "true-target")]
    public void EvaluateTransitions_WithBranchType_DispatchesOnlyMatchingIfElseBranch(
        bool isMatch,
        string expectedTarget,
        string skippedTarget)
    {
        using var definition = JsonDocument.Parse("""
        {
          "Steps": [
            { "Id": "if-node", "Type": "If" },
            { "Id": "true-target", "Type": "Log" },
            { "Id": "false-target", "Type": "Log" }
          ],
          "Transitions": [
            { "Id": "t-true", "Source": "if-node", "Target": "true-target", "BranchType": "true" },
            { "Id": "t-false", "Source": "if-node", "Target": "false-target", "BranchType": "false" }
          ]
        }
        """);
        using var context = JsonDocument.Parse($$"""
        {
          "Inputs": {},
          "Steps": { "if-node": { "Output": { "IsMatch": {{isMatch.ToString().ToLowerInvariant()}} } } },
          "Meta": {}
        }
        """);

        var transitions = _sut.EvaluateTransitions(definition, "if-node", context);

        Assert.Contains(transitions, transition =>
            transition.TargetNodeId == expectedTarget && transition.IsConditionMet);
        Assert.Contains(transitions, transition =>
            transition.TargetNodeId == skippedTarget && !transition.IsConditionMet);
        Assert.Single(transitions, transition => transition.IsConditionMet);
    }

    [Fact]
    public void EvaluateTransitions_WithBranchTypeAndMissingIfOutput_SkipsBothBranchesFailSafe()
    {
        using var definition = JsonDocument.Parse("""
        {
          "Steps": [{ "Id": "if-node", "Type": "If" }],
          "Transitions": [
            { "Id": "t-true", "Source": "if-node", "Target": "a", "BranchType": "true" },
            { "Id": "t-false", "Source": "if-node", "Target": "b", "BranchType": "false" }
          ]
        }
        """);
        using var context = JsonDocument.Parse("""{"Inputs":{},"Steps":{},"Meta":{}}""");

        var transitions = _sut.EvaluateTransitions(definition, "if-node", context);

        Assert.All(transitions, transition => Assert.False(transition.IsConditionMet));
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

    [Fact]
    public void FindStartNodeIds_WhenDefinitionHasMultipleIndependentStartNodes_ReturnsAllStartNodes()
    {
        using var definition = JsonDocument.Parse("""
        {
          "Steps": [
            { "Id": "manual", "Type": "ManualTrigger" },
            { "Id": "webhook", "Type": "WebhookTrigger" },
            { "Id": "log", "Type": "Log" }
          ],
          "Transitions": [
            { "Id": "t1", "Source": "manual", "Target": "log" },
            { "Id": "t2", "Source": "webhook", "Target": "log" }
          ]
        }
        """);

        var starts = _sut.FindStartNodeIds(definition);

        Assert.Equal(2, starts.Count);
        Assert.Contains("manual", starts);
        Assert.Contains("webhook", starts);
    }
}
