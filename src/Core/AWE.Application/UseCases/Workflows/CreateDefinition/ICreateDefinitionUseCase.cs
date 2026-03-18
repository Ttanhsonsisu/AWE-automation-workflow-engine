using System.Threading;
using System.Threading.Tasks;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.CreateDefinition;

public interface ICreateDefinitionUseCase
{
    Task<Result<CreateDefinitionResponse>> ExecuteAsync(CreateDefinitionRequest request, CancellationToken cancellationToken = default);
}
