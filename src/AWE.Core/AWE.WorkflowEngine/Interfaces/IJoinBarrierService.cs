using AWE.Domain.Entities;
using AWE.WorkflowEngine.Objects;

namespace AWE.WorkflowEngine.Interfaces;

public interface IJoinBarrierService
{
    Task<JoinBarrierResult> EvaluateBarrierAsync(WorkflowInstance instance, string joinNodeId, int totalIncomingEdges);
}
