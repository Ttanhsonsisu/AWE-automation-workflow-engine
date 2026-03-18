using System.Text.Json;
using AWE.Shared.Primitives;

namespace AWE.Application.Abstractions.CoreEngine;

public interface IWorkflowOrchestrator
{
    Task<Result<Guid>> StartWorkflowAsync(Guid definitionId, string jobName, string inputData, Guid? correlationId);

    Task<Result> HandleStepCompletionAsync(Guid instanceId, Guid executionPointerId, JsonDocument? output);
    Task<Result> HandleStepFailureAsync(Guid instanceId, Guid executionPointerId, string error);

    // API để đánh thức một Node đang ngủ (Wait)
    Task<Result> ResumeStepAsync(Guid pointerId, JsonDocument resumeData);
}
