using System.Text.Json;
using AWE.WorkflowEngine.Services;

namespace AWE.WorkflowEngine.Tests;

public class WorkflowContextManagerTests
{
    private readonly WorkflowContextManager _sut = new();

    [Fact]
    public void InitializeContext_WhenDefinitionHasContextSettings_MergesThemWithTriggerPayload()
    {
        using var defaults = JsonDocument.Parse("""
        {
          "applicationId": "default-id",
          "review": { "minimumGpa": 2.8, "queue": "standard" },
          "dryRun": true
        }
        """);

        var result = _sut.InitializeContext(
            """{"applicationId":"webhook-id","review":{"queue":"urgent"}}""",
            "Webhook job",
            Guid.NewGuid(),
            defaultInputData: defaults);

        Assert.True(result.IsSuccess);
        var inputs = result.Value.RootElement.GetProperty("Inputs");
        Assert.Equal("webhook-id", inputs.GetProperty("applicationId").GetString());
        Assert.Equal(2.8, inputs.GetProperty("review").GetProperty("minimumGpa").GetDouble());
        Assert.Equal("urgent", inputs.GetProperty("review").GetProperty("queue").GetString());
        Assert.True(inputs.GetProperty("dryRun").GetBoolean());
    }

    [Fact]
    public void InitializeContext_WhenTriggerPayloadIsEmpty_UsesDefinitionContextSettings()
    {
        using var defaults = JsonDocument.Parse("""{"sheetUrl":"https://example.test/sheet","maxRows":200}""");

        var result = _sut.InitializeContext(
            "{}",
            "Webhook job",
            Guid.NewGuid(),
            defaultInputData: defaults);

        Assert.True(result.IsSuccess);
        var inputs = result.Value.RootElement.GetProperty("Inputs");
        Assert.Equal("https://example.test/sheet", inputs.GetProperty("sheetUrl").GetString());
        Assert.Equal(200, inputs.GetProperty("maxRows").GetInt32());
    }
}
