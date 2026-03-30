using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence.Repositories;

public class PluginVersionRepository(ApplicationDbContext db) : IPluginVersionRepository
{
    private readonly ApplicationDbContext _db = db;

    public Task<bool> ExistsVersionAsync(Guid packageId, string version, CancellationToken ct = default)
        => _db.PluginVersions.AnyAsync(x => x.PackageId == packageId && x.Version == version, ct);

    public Task<PluginVersion?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.PluginVersions.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<PluginVersion>> ListByPackageIdAsync(Guid packageId, CancellationToken ct = default)
        => await _db.PluginVersions.AsNoTracking()
            .Where(x => x.PackageId == packageId)
            .OrderByDescending(x => x.CreatedAt) // hoặc sort semantic
            .ToListAsync(ct);

    public Task AddAsync(PluginVersion entity, CancellationToken ct = default)
        => _db.PluginVersions.AddAsync(entity, ct).AsTask();

    public void Remove(PluginVersion entity) => _db.PluginVersions.Remove(entity);

    public async Task<IReadOnlyList<PluginVersion>> ListActiveByPackageIdAsync(Guid packageId, CancellationToken ct = default)
        => await _db.PluginVersions
            .Where(x => x.PackageId == packageId && x.IsActive)
            .ToListAsync(ct);

    public async Task<PluginVersion?> GetBySha256Async(string sha256, CancellationToken ct)
    {
        return await _db.PluginVersions.Include(x => x.Package).FirstOrDefaultAsync(x => x.ExecutionMetadata.RootElement.GetProperty("Sha256").GetString() == sha256, ct);
    }
}
