using System.Text.Json;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Application.Dtos.WorkflowDto;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Consts;
using AWE.Shared.Extensions; 
using MassTransit;
using MassTransit.Transports;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Services;

public class PointerDispatcher(
    IVariableResolver resolver,
    ILogger<PointerDispatcher> logger,
    IMessageScheduler messageScheduler,
    IPublishEndpoint publishEndpoint) : IPointerDispatcher
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

        // =================================================================
        // Check Configuration & Resolve Variable
        if ((bool)!stepModel.IsConfigured)
        {
            
            // Cập nhật trạng thái Pointer thành Tạm dừng (Suspended)
            // Tùy vào Entity của bạn, có thể gán trực tiếp hoặc dùng method của Domain
            var errorDoc = JsonSerializer.SerializeToDocument(new
            {
                Message = "Node chưa được cấu hình đầy đủ. Vui lòng hoàn thiện cấu hình để tiếp tục."
            });

            pointer.Status = ExecutionPointerStatus.Suspended;
            pointer.Output = errorDoc;

            await PublishDispatchIssueAsync(
                instanceId: instance.Id,
                pointer: pointer,
                uiStatus: "Suspended",
                auditEvent: "StepSuspended",
                auditMessage: "Node chưa được cấu hình đầy đủ. Vui lòng hoàn thiện cấu hình để tiếp tục.",
                level: AWE.Domain.Enums.LogLevel.Warning,
                metadataJson: errorDoc.RootElement.GetRawText());

            return null;
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

            pointer.MarkAsFailedByEngine(errorDoc);

            // log lỗi chi tiết để dễ debug, bao gồm InstanceId, StepId và thông tin lỗi
            logger.LogError("Workflow {InstanceId} FAILED at Step {StepId}. {Error}", instance.Id, pointer.StepId, resolveResult.ErrorMessage);

            await PublishDispatchIssueAsync(
                instanceId: instance.Id,
                pointer: pointer,
                uiStatus: "Failed",
                auditEvent: "StepFailed",
                auditMessage: $"Node {pointer.StepId} thất bại ở phase resolve biến.",
                level: AWE.Domain.Enums.LogLevel.Error,
                metadataJson: errorDoc.RootElement.GetRawText());

            return null; 
        }

        string resolvedPayload = resolveResult.ResolvedPayload;

        try
        {
            pointer.SetInput(JsonDocument.Parse(resolvedPayload));
        }
        catch (JsonException)
        {
            pointer.SetInput(JsonDocument.Parse("{}"));
        }

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

            pointer.MarkAsFailedByEngine(errorDoc);

            logger.LogError("Payload limit exceeded for Step {StepId}. Size: {Size} bytes", pointer.StepId, payloadSize);

            await PublishDispatchIssueAsync(
                instanceId: instance.Id,
                pointer: pointer,
                uiStatus: "Failed",
                auditEvent: "StepFailed",
                auditMessage: $"Node {pointer.StepId} thất bại do payload vượt quá giới hạn cho phép.",
                level: AWE.Domain.Enums.LogLevel.Error,
                metadataJson: errorDoc.RootElement.GetRawText());

            return null;
        }

        // =================================================================
        // FR-11: HIBERNATE (WAIT & DELAY) - KHÔNG GỬI XUỐNG WORKER
        // =================================================================
        // Dùng StringComparison.OrdinalIgnoreCase để chống lỗi FE gửi "delay" hay "Delay"
        if (actualPluginType.Equals("Wait", StringComparison.OrdinalIgnoreCase) ||
            actualPluginType.Equals("Delay", StringComparison.OrdinalIgnoreCase))
        {
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

    private async Task PublishDispatchIssueAsync(
        Guid instanceId,
        ExecutionPointer pointer,
        string uiStatus,
        string auditEvent,
        string auditMessage,
        AWE.Domain.Enums.LogLevel level,
        string? metadataJson)
    {
        await publishEndpoint.Publish(new UiNodeStatusChangedEvent(
            InstanceId: instanceId,
            StepId: pointer.StepId,
            Status: uiStatus,
            Timestamp: DateTime.UtcNow));

        await publishEndpoint.Publish(new WriteAuditLogCommand(
            InstanceId: instanceId,
            Event: auditEvent,
            Message: auditMessage,
            Level: level,
            ExecutionPointerId: pointer.Id,
            NodeId: pointer.StepId,
            MetadataJson: metadataJson));
    }
}
