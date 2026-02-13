using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace AWE.WorkflowEngine.Interfaces;

public interface IWorkflowOrchestrator
{
    Task StartWorkflowAsync(Guid definitionId, string jobName, string inputData, Guid? correlationId);

    Task HandleStepCompletionAsync(Guid instanceId, Guid executionPointerId, JsonDocument? output);
    Task HandleStepFailureAsync(Guid instanceId, Guid executionPointerId, string error);
}
