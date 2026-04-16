using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.PublishDefinition;

public interface IPublishDefinitionUseCase
{
    Task<Result<PublishDefinitionResponse>> ExecuteAsync(PublishDefinitionRequest request, CancellationToken cancellationToken = default);
}
