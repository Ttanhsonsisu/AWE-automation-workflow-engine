using System.Threading;
using System.Threading.Tasks;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.DeleteDefinition;

public interface IDeleteDefinitionUseCase
{
    Task<Result> ExecuteAsync(DeleteDefinitionRequest request, CancellationToken cancellationToken = default);
}
