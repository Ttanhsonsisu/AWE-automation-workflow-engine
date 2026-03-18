using System.Threading;
using System.Threading.Tasks;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.ImportDefinition;

public interface IImportDefinitionUseCase
{
    Task<Result<ImportDefinitionResponse>> ExecuteAsync(ImportDefinitionRequest request, CancellationToken cancellationToken = default);
}
