using System.Text.Json;
using System.Text.Json.Nodes;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Domain.Entities;
using AWE.Shared.Primitives;

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
            ["Meta"] = new JsonObject
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

        /* xóa bỏ logic này vì trong luồng có luồng thực hiện vòng lặp và bỏ logic cho ghi đè dữ liệu
        // Đảm bảo node của từng StepId tồn tại
        // immutability check: nếu đã có Output rồi thì không được ghi đè (Zero-Contention)
        if (stepsNode.ContainsKey(stepId) && stepsNode[stepId] != null)
        {
            var existingStepData = stepsNode[stepId]!.AsObject();
            if (existingStepData.ContainsKey("Output") && existingStepData["Output"] != null)
            {
                // TẠI SAO PHẢI THROW EXCEPTION Ở ĐÂY?
                // Message Broker (MassTransit/RabbitMQ) đôi khi có thể gửi "Duplicate Message" (Gửi trùng 1 event 2 lần).
                // Nếu ta không chặn, Worker thứ 2 chạy xong sẽ ghi đè lên dữ liệu của Worker thứ 1.
                // Điều này phá vỡ tính toàn vẹn dữ liệu của hệ thống phân tán.
                throw new InvalidOperationException($"Immutability Violation: Node '{stepId}' đã có Output trong Context. Engine từ chối ghi đè dữ liệu để bảo vệ tính toàn vẹn (Zero-Contention).");
            }
        }
        else
        {
            stepsNode[stepId] = new JsonObject();
        }
        */

        if (!stepsNode.ContainsKey(stepId) || stepsNode[stepId] == null)
        {
            // Nếu chưa có, tạo node mới cho Step này
            stepsNode[stepId] = new JsonObject();
        }

        var stepData = stepsNode[stepId]!.AsObject();

        // Merge output vào
        stepData["Output"] = JsonNode.Parse(stepOutput.RootElement.GetRawText());

        // Gọi hàm của Entity để cập nhật lại Context
        instance.UpdateContext(JsonDocument.Parse(root.ToJsonString()));
    }
}
