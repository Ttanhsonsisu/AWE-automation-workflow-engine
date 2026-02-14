using AWE.WorkflowEngine.BackgroundServices;
using AWE.WorkflowEngine.Interfaces;
using AWE.WorkflowEngine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AWE.WorkflowEngine;

public static class DependencyInjection
{
    public static IServiceCollection AddWorkflowEngineService(this IServiceCollection services)
    {
        // add service
        services.AddScoped<IVariableResolver, VariableResolver>();
        services.AddScoped<IWorkflowOrchestrator, WorkflowOrchestrator>();

        // add backgroundservice
        services.AddHostedService<RecoveryBackgroundService>();

        return services;
    }
}
