using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.Persistence;

public interface IPluginVersionRepository
{
    Task<bool> ExistsVersionAsync(Guid packageId, string version, CancellationToken ct = default);
    Task<PluginVersion?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PluginVersion>> ListByPackageIdAsync(Guid packageId, CancellationToken ct = default);

    Task AddAsync(PluginVersion entity, CancellationToken ct = default);
    void Remove(PluginVersion entity);

    // nếu muốn enforce "chỉ 1 active/version trong package"
    Task<IReadOnlyList<PluginVersion>> ListActiveByPackageIdAsync(Guid packageId, CancellationToken ct = default);

    Task<PluginVersion?> GetBySha256Async(string sha256, CancellationToken ct);
}
