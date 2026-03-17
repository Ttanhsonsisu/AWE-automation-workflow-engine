using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence.Repositories;

public class PluginPackageRepository(ApplicationDbContext db) : IPluginPackageRepository
{
    private readonly ApplicationDbContext _db = db;

    public Task<bool> ExistsByUniqueNameAsync(string uniqueName, CancellationToken ct = default)
        => _db.PluginPackages.AnyAsync(x => x.UniqueName == uniqueName, ct);

    public Task<PluginPackage?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.PluginPackages.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<PluginPackage?> GetByUniqueNameAsync(string uniqueName, CancellationToken ct = default)
        => _db.PluginPackages.FirstOrDefaultAsync(x => x.UniqueName == uniqueName, ct);

    public Task AddAsync(PluginPackage entity, CancellationToken ct = default)
        => _db.PluginPackages.AddAsync(entity, ct).AsTask();

    public async Task<IReadOnlyList<PluginPackage>> ListAsync(CancellationToken ct = default)
        => await _db.PluginPackages.AsNoTracking().OrderBy(x => x.UniqueName).ToListAsync(ct);

    public async Task<bool> ExistsAsync(Guid packageId, CancellationToken ct)
        => await _db.PluginPackages.AnyAsync(x => x.Id.Equals(packageId), ct);
}
