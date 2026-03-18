using System.Text.Json;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.CoreEngine;

public interface IPointerDispatcher
{
    Task<ExecutePluginCommand?> CreateDispatchCommand(WorkflowInstance instance, ExecutionPointer pointer, JsonDocument defJson);
}
