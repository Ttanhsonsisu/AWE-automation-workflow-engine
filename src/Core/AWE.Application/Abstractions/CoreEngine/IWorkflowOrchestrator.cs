using System.Text.Json;
using AWE.Shared.Primitives;

namespace AWE.Application.Abstractions.CoreEngine;

public interface IWorkflowOrchestrator
{
    Task<Result<Guid>> StartWorkflowAsync(
        Guid definitionId,
        string jobName,
        string inputData,
        Guid? correlationId,
        bool isTest = false,
        string? stopAtStepId = null,
        string? idempotencyKey = null);

    Task<Result> HandleStepCompletionAsync(Guid instanceId, Guid executionPointerId, JsonDocument? output);
    Task<Result> HandleStepFailureAsync(Guid instanceId, Guid executionPointerId, string error);

    // API để đánh thức một Node đang ngủ (Wait)
    Task<Result> ResumeStepAsync(Guid pointerId, JsonDocument resumeData);

    // API để tạm dừng một Node đang chạy (ví dụ: khi phát hiện lỗi nghiêm trọng hoặc cần can thiệp thủ công)
    Task<Result> HandleStepSuspendedAsync(Guid instanceId, Guid pointerId, string? reason);
}
