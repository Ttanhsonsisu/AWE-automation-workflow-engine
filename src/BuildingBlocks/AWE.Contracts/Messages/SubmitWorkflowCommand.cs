namespace AWE.Contracts.Messages;

using System;
using System.Collections.Generic;
using System.Text.Json;
using AWE.Domain.Enums;

#region Workflow Submission

/// <summary>
/// Command to submit a workflow for execution.
/// </summary>
/// <remarks>
/// Published by API Gateway.
/// Consumed by Workflow Engine.
/// Represents an intent and may be rejected.
/// </remarks>
public record SubmitWorkflowCommand(
    Guid DefinitionId,        // Workflow definition to execute
    string JobName,           // Display name for this execution
    string InputData,         // Input payload (JSON)
    Guid? CorrelationId,       // Correlation identifier for end-to-end tracing,
    bool IsTest = false         // Whether this is a test run (affects logging and monitoring)
);

#endregion

#region Plugin Execution

/// <summary>
/// Command to execute a workflow step using a plugin.
/// </summary>
/// <remarks>
/// Published by Workflow Engine.
/// Consumed by Plugin Worker.
/// This command must be processed idempotently.
/// </remarks>
public record ExecutePluginCommand(
    Guid InstanceId,             // ID của Workflow Instance
    Guid ExecutionPointerId,     // [QUAN TRỌNG] Khóa chính dòng Pointer trong DB
    string NodeId,               // Tên bước trong JSON (VD: "Activity_1")
    string StepType,             // Loại Plugin (VD: "Http", "Email")
    string Payload,               // Dữ liệu JSON đã được Engine giải quyết biến
    PluginExecutionMode ExecutionMode = PluginExecutionMode.BuiltIn,
    string? DllPath = null,
    string? ExecutionMetadataJson = null
);

#endregion

#region Step Execution Events

/// <summary>
/// Event published when a workflow step has completed successfully.
/// </summary>
/// <remarks>
/// Published by Plugin Worker.
/// Consumed by Workflow Engine.
/// Event delivery is at-least-once; consumers must tolerate duplicates.
/// </remarks>
public record StepCompletedEvent(
    Guid WorkflowInstanceId,                          // Workflow instance identifier
    Guid ExecutionPointerId,
    string StepId,                              // Step instance identifier
    JsonDocument Output,     // Step execution output
    DateTime CompletedAt
);

/// <summary>
/// Event published when a workflow step has failed.
/// </summary>
/// <remarks>
/// Published by Plugin Worker.
/// Consumed by Workflow Engine.
/// Failure handling and compensation are delegated to the workflow engine.
/// </remarks>
public record StepFailedEvent(
    Guid InstanceId,          // Workflow instance identifier
    Guid ExecutionPointerId,
    string StepId,              // Step instance identifier
    string ErrorMessage,     // Failure description,
    DateTime? FailedAt = null
);

#endregion

#region Audit Logging

/// <summary>
/// Command to write an audit log entry.
/// </summary>
/// <remarks>
/// Published by any service.
/// Consumed by Audit service.
/// Audit logging must not affect the main execution flow.
/// </remarks>
/// <summary>
/// Lệnh ghi nhận lịch sử thực thi của Workflow.
/// Được bắn từ Engine hoặc Worker qua RabbitMQ.
/// </summary>
public record WriteAuditLogCommand(
    Guid InstanceId,                // BẮT BUỘC: ID của luồng Workflow để gom nhóm log
    string Event,                   // Sự kiện (VD: "StepStarted", "StepCompleted", "WorkflowFailed")
    string Message,                 // Mô tả ngắn gọn (VD: "Node HTTP Request chạy thành công")
    LogLevel Level = LogLevel.Information, // Mức độ (Error, Warning, Info)

    Guid? ExecutionPointerId = null, // ID của Pointer (nếu log này thuộc về 1 bước cụ thể)
    string? NodeId = null,           // Tên Step trên giao diện (VD: "Step_1_Start") - Rất quan trọng cho Frontend UI!
    string? WorkerId = null,         // Tên máy chủ/container đã chạy (VD: "Worker-DEVNM-76d2")

    string? MetadataJson = null,     // Chi tiết Input/Output hoặc mã lỗi (Lưu dạng chuỗi JSON)
    DateTime? Timestamp = null       // Thời gian xảy ra (Nên set mặc định là UtcNow nếu null)
)
{
    // Đảm bảo Timestamp luôn có giá trị nếu người gửi quên truyền
    public DateTime OccurredOn => Timestamp ?? DateTime.UtcNow;
}

#endregion
