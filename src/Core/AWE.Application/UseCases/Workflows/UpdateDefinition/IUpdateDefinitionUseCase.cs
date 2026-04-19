using System.Threading;
using System.Threading.Tasks;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.UpdateDefinition;

public interface IUpdateDefinitionUseCase
{
    Task<Result<UpdateDefinitionResponse>> ExecuteAsync(UpdateDefinitionRequest request, CancellationToken cancellationToken = default);
}
