using System.Text.Json;
using AWE.Domain.Entities;

namespace AWE.WorkflowEngine.Interfaces;

public interface IPointerDispatcher
{
    Task DispatchAsync(WorkflowInstance instance, ExecutionPointer pointer, JsonDocument defJson);
}
