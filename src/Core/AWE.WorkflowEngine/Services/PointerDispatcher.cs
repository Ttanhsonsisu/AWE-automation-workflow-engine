using System.Text.Json;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Application.Dtos.WorkflowDto;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Consts;
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

        // Nội suy (áp dụng strict mode: nếu thiếu biến nào sẽ trả về nguyên gốc và báo lỗi, không im lặng bỏ qua)
        var resolveResult = resolver.Resolve(rawInputs, instance.ContextData);

        if (!resolveResult.IsSuccess)
        {

            var errorDoc = JsonSerializer.SerializeToDocument(new
            {
                ErrorCode = "VARIABLE_RESOLUTION_FAILED",
                Message = resolveResult.ErrorMessage,
                MissingVariables = resolveResult.MissingVariables,
                FailedAt = DateTime.UtcNow
            });

            pointer.MarkAsFailed(pointer.Id.ToString(), errorDoc);

            // log lỗi chi tiết để dễ debug, bao gồm InstanceId, StepId và thông tin lỗi
            logger.LogError("Workflow {InstanceId} FAILED at Step {StepId}. {Error}", instance.Id, pointer.StepId, resolveResult.ErrorMessage);

            return null; 
        }

        string resolvedPayload = resolveResult.ResolvedPayload;

        // =================================================================
        // 2. SIZE CONTROL GUARD (Bảo vệ Message Queue)
        // =================================================================
        int payloadSize = System.Text.Encoding.UTF8.GetByteCount(resolvedPayload);
        if (payloadSize > Consts.MAX_PAYLOAD_SIZE_BYTES)
        {
            var errorDoc = JsonSerializer.SerializeToDocument(new
            {
                ErrorCode = "PAYLOAD_TOO_LARGE",
                Message = $"Kích thước Payload vượt ngưỡng cho phép (1MB). Vui lòng sử dụng cơ chế truyền File URI.",
                ActualSizeBytes = payloadSize,
                LimitBytes = Consts.MAX_PAYLOAD_SIZE_BYTES,
                FailedAt = DateTime.UtcNow
            });

            pointer.MarkAsFailed(pointer.Id.ToString(), errorDoc);

            logger.LogError("Payload limit exceeded for Step {StepId}. Size: {Size} bytes", pointer.StepId, payloadSize);
            return null;
        }

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

                // fix read json đã nội suy, có thể là số hoặc chuỗi, và có thể có nhiều cách viết khác nhau của "Seconds"
                try
                {
                    using var resolvedDoc = JsonDocument.Parse(resolvedPayload);
                    if (resolvedDoc.RootElement.TryGetPropertyCaseInsensitive("Seconds", out var secElem))
                    {
                        if (secElem.ValueKind == JsonValueKind.Number) delaySeconds = secElem.GetInt32();
                        else if (secElem.ValueKind == JsonValueKind.String && int.TryParse(secElem.GetString(), out int parsed)) delaySeconds = parsed;
                    }

                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Failed to parse delay Seconds for Step {StepId}. Fallback to 60s.", pointer.StepId);
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
