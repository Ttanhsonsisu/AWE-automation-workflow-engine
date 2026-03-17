using System.Text.Json;
using System.Text.Json.Nodes;
using AWE.Domain.Entities;
using AWE.Shared.Primitives;
using AWE.WorkflowEngine.Interfaces;

namespace AWE.WorkflowEngine.Services;

public class WorkflowContextManager : IWorkflowContextManager
{
    public Result<JsonDocument> InitializeContext(string inputData, string jobName, Guid correlationId)
    {
        JsonNode? inputsNode;
        try
        {
            var rawInput = string.IsNullOrWhiteSpace(inputData) ? "{}" : inputData;
            inputsNode = JsonNode.Parse(rawInput);
        }
        catch (JsonException)
        {
            return Result.Failure<JsonDocument>(Error.Validation("Input.InvalidJson", "Input data is not valid JSON"));
        }

        var initialContext = new JsonObject
        {
            ["Inputs"] = inputsNode,
            ["Steps"] = new JsonObject(),
            ["System"] = new JsonObject
            {
                ["CorrelationId"] = correlationId,
                ["JobName"] = jobName,
                ["StartedAt"] = DateTime.UtcNow
            }
        };

        return Result.Success(JsonDocument.Parse(initialContext.ToJsonString()));
    }

    public void MergeStepOutput(WorkflowInstance instance, string stepId, JsonDocument? stepOutput)
    {
        if (stepOutput == null) return;

        var root = JsonNode.Parse(instance.ContextData.RootElement.GetRawText())!.AsObject();

        // Đảm bảo node "Steps" tồn tại
        if (!root.ContainsKey("Steps") || root["Steps"] == null)
            root["Steps"] = new JsonObject();

        var stepsNode = root["Steps"]!.AsObject();

        // Đảm bảo node của từng StepId tồn tại
        if (!stepsNode.ContainsKey(stepId) || stepsNode[stepId] == null)
            stepsNode[stepId] = new JsonObject();

        var stepData = stepsNode[stepId]!.AsObject();

        // Merge output vào
        stepData["Output"] = JsonNode.Parse(stepOutput.RootElement.GetRawText());

        // Gọi hàm của Entity để cập nhật lại Context
        instance.UpdateContext(JsonDocument.Parse(root.ToJsonString()));
    }
}
