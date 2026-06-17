using System.Text.Json;
using AWE.WorkflowEngine.Services;

namespace AWE.WorkflowEngine.Tests;

public class VariableResolverTests
{
    private readonly VariableResolver _sut = new();

    [Fact]
    public void Resolve_WhenPayloadUsesWorkflowInputStepOutputAndSystemVariables_ReplacesAllVariables()
    {
        using var context = JsonDocument.Parse("""
        {
          "Inputs": {
            "message": "hello \"AWE\"",
            "score": 90,
            "approved": true,
            "payload": { "nested": 1 }
          },
          "Steps": {
            "retry": {
              "Output": {
                "Status": "Succeeded",
                "Attempt": 3
              }
            }
          },
          "Meta": {
            "instanceId": "inst-001"
          }
        }
        """);

        var rawPayload = """
        {
          "message": "{{workflow.input.message}}",
          "score": {{workflow.input.score}},
          "approved": {{workflow.input.approved}},
          "payload": {{workflow.input.payload}},
          "retryStatus": "{{steps.retry.output.Status}}",
          "retryAttempt": {{steps.retry.output.Attempt}},
          "instanceId": "{{workflow.system.instanceId}}"
        }
        """;

        var result = _sut.Resolve(rawPayload, context);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.MissingVariables);

        using var resolved = JsonDocument.Parse(result.ResolvedPayload);
        var root = resolved.RootElement;
        Assert.Equal("hello \"AWE\"", root.GetProperty("message").GetString());
        Assert.Equal(90, root.GetProperty("score").GetInt32());
        Assert.True(root.GetProperty("approved").GetBoolean());
        Assert.Equal(1, root.GetProperty("payload").GetProperty("nested").GetInt32());
        Assert.Equal("Succeeded", root.GetProperty("retryStatus").GetString());
        Assert.Equal(3, root.GetProperty("retryAttempt").GetInt32());
        Assert.Equal("inst-001", root.GetProperty("instanceId").GetString());
    }

    [Fact]
    public void Resolve_WhenVariableIsMissing_ReturnsFailureAndKeepsOriginalPayload()
    {
        using var context = JsonDocument.Parse("""
        {
          "Inputs": { "message": "hello" },
          "Steps": {},
          "Meta": {}
        }
        """);
        var rawPayload = """{"message":"{{workflow.input.message}}","score":{{workflow.input.score}}}""";

        var result = _sut.Resolve(rawPayload, context);

        Assert.False(result.IsSuccess);
        Assert.Equal(rawPayload, result.ResolvedPayload);
        Assert.Contains("workflow.input.score", result.MissingVariables);
        Assert.Contains("workflow.input.score", result.ErrorMessage);
    }

    [Fact]
    public void Resolve_WhenRawPayloadIsEmpty_ReturnsEmptyJsonObject()
    {
        using var context = JsonDocument.Parse("""{"Inputs":{},"Steps":{},"Meta":{}}""");

        var result = _sut.Resolve("", context);

        Assert.True(result.IsSuccess);
        Assert.Equal("{}", result.ResolvedPayload);
        Assert.Empty(result.MissingVariables);
    }
}
