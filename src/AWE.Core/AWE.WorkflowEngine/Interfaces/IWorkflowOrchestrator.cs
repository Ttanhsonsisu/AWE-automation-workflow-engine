using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using AWE.Shared.Primitives;

namespace AWE.WorkflowEngine.Interfaces;

public interface IWorkflowOrchestrator
{
    Task<Result<Guid>> StartWorkflowAsync(Guid definitionId, string jobName, string inputData, Guid? correlationId);

    Task<Result> HandleStepCompletionAsync(Guid instanceId, Guid executionPointerId, JsonDocument? output);
    Task<Result> HandleStepFailureAsync(Guid instanceId, Guid executionPointerId, string error);
}
