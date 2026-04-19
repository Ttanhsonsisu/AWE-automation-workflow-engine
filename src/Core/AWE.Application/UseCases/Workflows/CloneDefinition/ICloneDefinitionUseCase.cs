using System;
using System.Threading;
using System.Threading.Tasks;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.CloneDefinition;

public interface ICloneDefinitionUseCase
{
    Task<Result<CloneDefinitionResponse>> ExecuteAsync(CloneDefinitionRequest request, CancellationToken cancellationToken = default);
}
