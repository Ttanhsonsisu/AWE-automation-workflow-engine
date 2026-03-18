using System.Text.Json;
using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.CoreEngine;

public interface IPointerDispatcher
{
    Task DispatchAsync(WorkflowInstance instance, ExecutionPointer pointer, JsonDocument defJson);
}
