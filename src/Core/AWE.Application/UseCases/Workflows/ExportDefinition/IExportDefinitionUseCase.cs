using System.Threading;
using System.Threading.Tasks;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.ExportDefinition;

public interface IExportDefinitionUseCase
{
    Task<Result<ExportDefinitionResponse>> ExecuteAsync(ExportDefinitionRequest request, CancellationToken cancellationToken = default);
}
