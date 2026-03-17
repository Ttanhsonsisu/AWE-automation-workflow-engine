using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using AWE.Domain.Entities;
using AWE.Shared.Primitives;

namespace AWE.WorkflowEngine.Interfaces;

public interface IWorkflowContextManager
{
    Result<JsonDocument> InitializeContext(string inputData, string jobName, Guid correlationId);
    void MergeStepOutput(WorkflowInstance instance, string stepId, JsonDocument? stepOutput);
}
