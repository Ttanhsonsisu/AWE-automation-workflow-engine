using System;
using System.Collections.Generic;
using System.Text;

namespace AWE.Contracts.Messages;

public record CompensatePluginCommand(
    Guid InstanceId,
    Guid ExecutionPointerId, // Pointer của step ĐÃ HOÀN THÀNH trước đó
    string NodeId,
    string StepType,
    string Payload // Output/Input cũ để Plugin biết đường mà Rollback
);
