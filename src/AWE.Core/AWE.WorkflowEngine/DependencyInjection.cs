using AWE.WorkflowEngine.Interfaces;
using AWE.WorkflowEngine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AWE.WorkflowEngine;

public static class DependencyInjection
{
    public static IServiceCollection AddWorkflowEngineService(this IServiceCollection services)
    {
        services.AddScoped<IVariableResolver, VariableResolver>();
        services.AddScoped<IWorkflowOrchestrator, WorkflowOrchestrator>();

        return services;
    }
}
