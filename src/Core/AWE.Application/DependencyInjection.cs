using Microsoft.Extensions.DependencyInjection;
using AWE.Application.UseCases.Workflows.CreateDefinition;
using AWE.Application.UseCases.Workflows.CloneDefinition;
using AWE.Application.UseCases.Workflows.DeleteDefinition;
using AWE.Application.UseCases.Workflows.UpdateDefinition;
using AWE.Application.UseCases.Workflows.ExportDefinition;
using AWE.Application.UseCases.Workflows.ImportDefinition;

using AWE.Application.UseCases.Executions.GetExecutions;
using AWE.Application.UseCases.Executions.GetExecutionDetails;
using AWE.Application.UseCases.Executions.GetExecutionLogs;
using AWE.Application.UseCases.Executions.SuspendExecution;
using AWE.Application.UseCases.Executions.ResumeExecution;
using AWE.Application.UseCases.Executions.RetryExecution;

namespace AWE.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAweApplication(this IServiceCollection services)
    {
        // Workflow Management
        services.AddScoped<ICreateDefinitionUseCase, CreateDefinitionUseCase>();
        services.AddScoped<ICloneDefinitionUseCase, CloneDefinitionUseCase>();
        services.AddScoped<IDeleteDefinitionUseCase, DeleteDefinitionUseCase>();
        services.AddScoped<IUpdateDefinitionUseCase, UpdateDefinitionUseCase>();
        services.AddScoped<IExportDefinitionUseCase, ExportDefinitionUseCase>();
        services.AddScoped<IImportDefinitionUseCase, ImportDefinitionUseCase>();

        // Execution Management
        services.AddScoped<IGetExecutionsUseCase, GetExecutionsUseCase>();
        services.AddScoped<IGetExecutionDetailsUseCase, GetExecutionDetailsUseCase>();
        services.AddScoped<IGetExecutionLogsUseCase, GetExecutionLogsUseCase>();
        services.AddScoped<ISuspendExecutionUseCase, SuspendExecutionUseCase>();
        services.AddScoped<IResumeExecutionUseCase, ResumeExecutionUseCase>();
        services.AddScoped<IRetryExecutionUseCase, RetryExecutionUseCase>();

        return services;
    }
}

