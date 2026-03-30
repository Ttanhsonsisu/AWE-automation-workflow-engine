using AWE.Domain.Enums;

namespace AWE.Contracts.Messages;

public record CompensatePluginCommand(
    Guid InstanceId,
    Guid ExecutionPointerId, // Pointer của step ĐÃ HOÀN THÀNH trước đó
    string NodeId,
    string StepType,
    string Payload, // Output/Input cũ để Plugin biết đường mà Rollback
    PluginExecutionMode ExecutionMode, // [THÊM]
    //string? DllPath = null,            // [THÊM]
    string? ExecutionMetadata = null // Chuỗi JSON chứa Sha256, Bucket, PluginType...
);
