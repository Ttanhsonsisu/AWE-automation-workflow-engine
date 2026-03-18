using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.CoreEngine;

public record JoinBarrierResult(

    bool IsBarrierBroken,           // Barrier đã vỡ chưa? (Đủ nhánh chưa)
    bool IsDeadPath,                // Tất cả các nhánh vào đều là Skipped?
    ExecutionPointer? PointerToDispatch // Pointer đại diện để chạy tiếp (nếu có)
);
