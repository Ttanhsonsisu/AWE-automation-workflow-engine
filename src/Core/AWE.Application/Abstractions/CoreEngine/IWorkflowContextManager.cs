using System.Text.Json;
using AWE.Domain.Entities;
using AWE.Shared.Primitives;

namespace AWE.Application.Abstractions.CoreEngine;

public interface IWorkflowContextManager
{
    Result<JsonDocument> InitializeContext(string inputData, string jobName, Guid correlationId, string? stopAtStepId = null);
    void MergeStepOutput(WorkflowInstance instance, string stepId, JsonDocument? stepOutput);
}
