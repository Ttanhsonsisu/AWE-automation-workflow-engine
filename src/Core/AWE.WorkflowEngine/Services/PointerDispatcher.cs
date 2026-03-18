using System.Text.Json;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Consts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Services;

public class PointerDispatcher(IVariableResolver resolver, ILogger<PointerDispatcher> logger) : IPointerDispatcher
{
    private readonly IVariableResolver _resolver = resolver;
    private readonly ILogger<PointerDispatcher> _logger = logger;

    public async Task<ExecutePluginCommand?> CreateDispatchCommand(WorkflowInstance instance, ExecutionPointer pointer, JsonDocument defJson)
    {
        var stepDef = GetStepDefinition(defJson, pointer.StepId);
        string stepType = stepDef.GetProperty("Type").GetString()!;


        // Logic Resolve Variable
        var rawInputs = stepDef.TryGetProperty("Inputs", out var inputsElem) ? inputsElem.GetRawText() : "{}";
        var resolvedPayload = _resolver.Resolve(rawInputs, instance.ContextData);
        // =================================================================
        // FR-11: HIBERNATE (WAIT & DELAY) - KHÔNG GỬI XUỐNG WORKER
        // =================================================================
        if (stepType == "Wait" || stepType == "Delay")
        {
            pointer.Status = ExecutionPointerStatus.WaitingForEvent;

            if (stepType == "Delay")
            {
                var inputDict = JsonSerializer.Deserialize<Dictionary<string, object>>(resolvedPayload) ?? new();

                // Giả sử input có trường "seconds" quy định số giây cần chờ
                int delaySeconds = 60; // Mặc định 1 phút nếu không truyền
                if (inputDict.TryGetValue("seconds", out var secObj) && int.TryParse(secObj.ToString(), out int parsedSec))
                {
                    delaySeconds = parsedSec;
                }

                // add time buffer 5s để đảm bảo Worker không bị đánh thức quá sớm do trễ mạng hoặc load cao
                pointer.HibernateUntil(DateTime.UtcNow.AddSeconds(delaySeconds));
                _logger.LogInformation("⏳ Workflow {InstanceId} HIBERNATED at Step {StepId}. Will wake up at {ResumeAt}", instance.Id, pointer.StepId, pointer.ResumeAt);
            }
            else
            {
                // Với Step "Wait", chúng ta sẽ đợi API bên ngoài gọi vào Resume, nên không set HibernateUntil mà chỉ đơn giản là chuyển trạng thái và chờ.
                pointer.PauseForWebhook();
                _logger.LogInformation("Workflow {InstanceId} PAUSED at Step {StepId} (Waiting for Webhook).", instance.Id, pointer.StepId);
            }

            return null;
        }

        // =================================================================
        // ĐỌC CẤU HÌNH EXECUTION MODE & DLL PATH
        // =================================================================
        PluginExecutionMode executionMode = PluginExecutionMode.BuiltIn; // Mặc định là chạy Built-in

        if (stepDef.TryGetProperty("ExecutionMode", out var modeElem))
        {
            if (modeElem.ValueKind == JsonValueKind.Number && modeElem.TryGetInt32(out int modeInt))
            {
                executionMode = (PluginExecutionMode)modeInt;
            }
            else if (modeElem.ValueKind == JsonValueKind.String && Enum.TryParse<PluginExecutionMode>(modeElem.GetString(), true, out var parsedMode))
            {
                executionMode = parsedMode;
            }
        }

        string? dllPath = stepDef.TryGetProperty("DllPath", out var dllElem) ? dllElem.GetString() : null;

        // update pointer status to Running before dispatching to ensure visibility in case of quick execution
        return new ExecutePluginCommand(
            InstanceId: instance.Id,
            ExecutionPointerId: pointer.Id,
            NodeId: pointer.StepId,
            StepType: stepType,
            Payload: resolvedPayload,
            ExecutionMode: executionMode,
            DllPath: dllPath
        );

    }

    private JsonElement GetStepDefinition(JsonDocument defJson, string stepId)
    {
        foreach (var step in defJson.RootElement.GetProperty("Steps").EnumerateArray())
        {
            if (step.GetProperty("Id").GetString() == stepId) return step;
        }
        throw new InvalidOperationException($"Step {stepId} not found");
    }
}
