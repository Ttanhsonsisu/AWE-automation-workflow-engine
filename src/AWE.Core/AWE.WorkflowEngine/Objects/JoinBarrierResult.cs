
using AWE.Domain.Entities;

namespace AWE.WorkflowEngine.Objects;

public record JoinBarrierResult(

    bool IsBarrierBroken,           // Barrier đã vỡ chưa? (Đủ nhánh chưa)
    bool IsDeadPath,                // Tất cả các nhánh vào đều là Skipped?
    ExecutionPointer? PointerToDispatch // Pointer đại diện để chạy tiếp (nếu có)
);
