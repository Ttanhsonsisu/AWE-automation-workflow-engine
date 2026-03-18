using System.Text.Json;
using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.CoreEngine;

public interface IWorkflowCompensationService
{
    Task TriggerCompensationAsync(WorkflowInstance instance, JsonDocument defJson);
}
