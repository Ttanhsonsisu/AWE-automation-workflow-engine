using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.Persistence;

public interface IPluginPackageRepository
{
    Task<bool> ExistsByUniqueNameAsync(string uniqueName, CancellationToken ct = default);
    Task<PluginPackage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PluginPackage?> GetByUniqueNameAsync(string uniqueName, CancellationToken ct = default);

    Task AddAsync(PluginPackage entity, CancellationToken ct = default);
    Task<IReadOnlyList<PluginPackage>> ListAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid packageId, CancellationToken ct);
}
