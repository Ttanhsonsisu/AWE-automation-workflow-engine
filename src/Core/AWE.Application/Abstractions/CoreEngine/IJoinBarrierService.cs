using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.CoreEngine;

public interface IJoinBarrierService
{
    Task<JoinBarrierResult> EvaluateBarrierAsync(WorkflowInstance instance, string joinNodeId, int totalIncomingEdges);
}
