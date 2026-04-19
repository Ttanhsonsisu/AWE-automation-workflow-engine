using System.Text.Json;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.CoreEngine;

public interface IWorkflowCompensationService
{
    Task<List<CompensatePluginCommand>> TriggerCompensationAsync(WorkflowInstance instance, JsonDocument defJson);
}
