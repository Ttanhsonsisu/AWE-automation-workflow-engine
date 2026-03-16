using System.Text.Json;
using AWE.Domain.Entities;

namespace AWE.WorkflowEngine.Interfaces;

public interface IWorkflowCompensationService
{
    Task TriggerCompensationAsync(WorkflowInstance instance, JsonDocument defJson);
}
