using AWE.Application.UseCases.Approvals.SubmitApproval;
using AWE.Application.UseCases.Audit;
using AWE.Application.UseCases.Executions.CancelExecution;
using AWE.Application.UseCases.Executions.GetExecutionDetails;
using AWE.Application.UseCases.Executions.GetExecutionLogs;
using AWE.Application.UseCases.Executions.GetExecutions;
using AWE.Application.UseCases.Executions.ResumeExecution;
using AWE.Application.UseCases.Executions.RetryExecution;
using AWE.Application.UseCases.Executions.SuspendExecution;
using AWE.Application.UseCases.Monitor.Daskboard;
using AWE.Application.UseCases.Workflows.CloneDefinition;
using AWE.Application.UseCases.Workflows.CreateDefinition;
using AWE.Application.UseCases.Workflows.DeleteDefinition;
using AWE.Application.UseCases.Workflows.ExportDefinition;
using AWE.Application.UseCases.Workflows.ImportDefinition;
using AWE.Application.UseCases.Workflows.PublishDefinition;
using AWE.Application.UseCases.Workflows.UnpublishDefinition;
using AWE.Application.UseCases.Workflows.UpdateDefinition;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddScoped<IPublishDefinitionUseCase, PublishDefinitionUseCase>();
        services.AddScoped<IUnpublishDefinitionUseCase, UnpublishDefinitionUseCase>();
        services.AddScoped<IExportDefinitionUseCase, ExportDefinitionUseCase>();
        services.AddScoped<IImportDefinitionUseCase, ImportDefinitionUseCase>();
        services.AddScoped<IGetDashboardMetricsQueryHandler, GetDashboardMetricsQueryHandler>();

        // Execution Management
        services.AddScoped<IGetExecutionsUseCase, GetExecutionsUseCase>();
        services.AddScoped<IGetExecutionDetailsUseCase, GetExecutionDetailsUseCase>();
        services.AddScoped<IGetExecutionLogsUseCase, GetExecutionLogsUseCase>();
        services.AddScoped<ISuspendExecutionUseCase, SuspendExecutionUseCase>();
        services.AddScoped<IResumeExecutionUseCase, ResumeExecutionUseCase>();
        services.AddScoped<IRetryExecutionUseCase, RetryExecutionUseCase>();
        services.AddScoped<ICancelExecutionUseCase, CancelExecutionUseCase>();

        // Audit log
        services.AddScoped<IGetAuditHistoryQueryHandler, GetAuditHistoryQueryHandler>();

        // Approvals
        services.AddScoped<ISubmitApprovalUseCase, SubmitApprovalUseCase>();

        return services;
    }
}

