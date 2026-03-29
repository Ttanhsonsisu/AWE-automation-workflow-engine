using System.Text.Json;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Application.Dtos.WorkflowDto;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Extensions; 
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Services;

public class PointerDispatcher(
    IVariableResolver resolver,
    ILogger<PointerDispatcher> logger,
    IMessageScheduler messageScheduler) : IPointerDispatcher
{
    public async Task<ExecutePluginCommand?> CreateDispatchCommand(WorkflowInstance instance, ExecutionPointer pointer, JsonDocument defJson)
    {
        // 1. Dùng Model để parse toàn bộ cấu trúc dễ dàng, không sợ lệch chuẩn
        var definition = WorkflowDefinitionModel.Parse(defJson.RootElement.GetRawText());

        var stepModel = definition.Steps.FirstOrDefault(s => s.Id == pointer.StepId);
        if (stepModel == null)
        {
            throw new InvalidOperationException($"Step {pointer.StepId} không tồn tại trong Definition.");
        }

        // 2. Lấy Tên Plugin THẬT SỰ (Xuyên qua cả BuiltIn lẫn DynamicDll)
        string actualPluginType = stepModel.GetActualPluginType();

        // 3. Logic Resolve Variable (Nội suy biến)
        var rawInputs = stepModel.Inputs.ValueKind == JsonValueKind.Object ? stepModel.Inputs.GetRawText() : "{}";
        var resolvedPayload = resolver.Resolve(rawInputs, instance.ContextData);

        // =================================================================
        // FR-11: HIBERNATE (WAIT & DELAY) - KHÔNG GỬI XUỐNG WORKER
        // =================================================================
        // Dùng StringComparison.OrdinalIgnoreCase để chống lỗi FE gửi "delay" hay "Delay"
        if (actualPluginType.Equals("Wait", StringComparison.OrdinalIgnoreCase) ||
            actualPluginType.Equals("Delay", StringComparison.OrdinalIgnoreCase))
        {
            pointer.Status = ExecutionPointerStatus.WaitingForEvent;

            if (actualPluginType.Equals("Delay", StringComparison.OrdinalIgnoreCase))
            {
                int delaySeconds = 60; 

                //fix lỗi gửi "Seconds", "seconds" hay "sEcOndS"
                if (stepModel.Inputs.TryGetPropertyCaseInsensitive("Seconds", out var secElem))
                {
                    // Lấy an toàn dù FE gửi số 5 hay chuỗi "5"
                    if (secElem.ValueKind == JsonValueKind.Number) delaySeconds = secElem.GetInt32();
                    else if (secElem.ValueKind == JsonValueKind.String && int.TryParse(secElem.GetString(), out int parsed)) delaySeconds = parsed;
                }

                var wakeupTime = DateTime.UtcNow.AddSeconds(delaySeconds);

                await messageScheduler.SchedulePublish(wakeupTime, new ResumeStepCommand(
                    InstanceId: instance.Id,
                    PointerId: pointer.Id,
                    StepId: pointer.StepId
                ));

                pointer.HibernateUntil(wakeupTime); 
                logger.LogInformation("Workflow {InstanceId} HIBERNATED at Step {StepId}. Will wake up at {ResumeAt}", instance.Id, pointer.StepId, pointer.ResumeAt);
            }
            else
            {
                pointer.PauseForWebhook();
                logger.LogInformation("Workflow {InstanceId} PAUSED at Step {StepId} (Waiting for Webhook).", instance.Id, pointer.StepId);
            }

            return null;
        }

        // =================================================================
        // ĐÓNG GÓI MESSAGE (Gửi xuống cho Worker chạy)
        // =================================================================

        // Chuẩn hóa Metadata thành String để gửi qua Message Queue
        string? executionMetadataJson = stepModel.ExecutionMetadata?.GetRawText();

        return new ExecutePluginCommand(
            InstanceId: instance.Id,
            ExecutionPointerId: pointer.Id,
            NodeId: pointer.StepId,
            StepType: actualPluginType, 
            Payload: resolvedPayload,
            ExecutionMode: stepModel.ExecutionMode,
            ExecutionMetadataJson: executionMetadataJson
        );
    }
}
