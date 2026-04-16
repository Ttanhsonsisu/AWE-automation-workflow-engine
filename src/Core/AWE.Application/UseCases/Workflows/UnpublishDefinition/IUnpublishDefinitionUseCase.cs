using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.UnpublishDefinition;

public interface IUnpublishDefinitionUseCase
{
    Task<Result<UnpublishDefinitionResponse>> ExecuteAsync(UnpublishDefinitionRequest request, CancellationToken cancellationToken = default);
}
