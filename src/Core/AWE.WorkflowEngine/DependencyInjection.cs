using AWE.Application.Abstractions.CoreEngine;
using AWE.Sdk;
using AWE.WorkflowEngine.BackgroundServices;
using AWE.WorkflowEngine.BuiltInPlugins;
using AWE.WorkflowEngine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AWE.WorkflowEngine;

public static class DependencyInjection
{
    public static IServiceCollection AddWorkflowEngineService(this IServiceCollection services)
    {
        // add service
        services.AddScoped<IVariableResolver, VariableResolver>();
        services.AddScoped<ITransitionEvaluator, TransitionEvaluator>();
        services.AddScoped<IPointerDispatcher, PointerDispatcher>();
        services.AddScoped<IWorkflowCompensationService, WorkflowCompensationService>();
        services.AddScoped<IWorkflowContextManager, WorkflowContextManager>();
        services.AddScoped<IJoinBarrierService, JoinBarrierService>();
        services.AddScoped<IWorkflowOrchestrator, WorkflowOrchestrator>();

        // add backgroundservice
        services.AddHostedService<RecoveryBackgroundService>();
        services.AddHostedService<PluginGarbageCollectorService>();
        //services.AddHostedService<DelayWakeUpBackgroundService>();
        //services.AddHostedService<CronSchedulePublisherService>();

        // add built-in plugins
        services.AddScoped<IPluginRegistry, PluginRegistry>();
        services.AddTransient<IWorkflowPlugin, LogPlugin>();
        services.AddTransient<IWorkflowPlugin, DelayPlugin>();
        services.AddTransient<IWorkflowPlugin, JoinPlugin>();
        services.AddTransient<IWorkflowPlugin, ApprovalPlugin>();

        return services;
    }
}
