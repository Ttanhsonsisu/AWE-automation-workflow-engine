namespace AWE.Contracts.Messages;

using System;
using System.Collections.Generic;

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
    Guid? CorrelationId       // Correlation identifier for end-to-end tracing
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
    Guid InstanceId,          // Workflow instance identifier
    Guid StepId,              // Step instance identifier
    string StepType,          // Plugin routing key (e.g. Video, Email)
    string Payload            // Plugin-specific configuration or data
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
    Guid InstanceId,                          // Workflow instance identifier
    Guid StepId,                              // Step instance identifier
    Dictionary<string, object> OutputData     // Step execution output
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
    Guid StepId,              // Step instance identifier
    string ErrorMessage       // Failure description
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
public record WriteAuditLogCommand(
    string Source,            // Originating service or component
    string Action,            // Performed action (e.g. StepStarted, Error)
    string Details,           // Action details (JSON or plain text)
    DateTime Timestamp,       // Event occurrence time (UTC)
    Guid? RelatedId           // Related workflow or step identifier
);

#endregion
