using System;
using System.Collections.Generic;
using System.Text;
using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Monitor.Daskboard;

public interface IGetDashboardMetricsQueryHandler
{
    Task<Result<DashboardMetricsResponse>> HandleAsync(CancellationToken cancellationToken);
}

public class GetDashboardMetricsQueryHandler(IWorkflowDefinitionRepository definitionRepository, IWorkflowInstanceRepository instanceRepository) : IGetDashboardMetricsQueryHandler
{
    private readonly IWorkflowDefinitionRepository _definitionRepository = definitionRepository;

    public async Task<Result<DashboardMetricsResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var response = await _definitionRepository.GetDashboardMetricsAsync(cancellationToken);

        return Result.Success(response);
    }
}
