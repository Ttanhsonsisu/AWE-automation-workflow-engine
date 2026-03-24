using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace AWE.Infrastructure.Persistence.Repositories;

public class ApprovalTokenRepository(ApplicationDbContext context) : IApprovalTokenRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task CreateToken(ApprovalToken token, CancellationToken cancellationToken = default)
    {
        await _context.ApprovalTokens.AddAsync(token);
    }

    public void DeleteTokenAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        _context.ApprovalTokens.Remove(new ApprovalToken { Id = tokenId });

    }

    public async Task<ApprovalToken?> GetByPointerIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ApprovalTokens.FirstOrDefaultAsync(x => x.PointerId == id, cancellationToken);
    }

    public async Task<ApprovalToken?> GetByTokenStringAsync(string token, CancellationToken cancellationToken = default)
    {
        return await _context.ApprovalTokens.FirstOrDefaultAsync(x => x.TokenString == token, cancellationToken);
    }

    public Task UpdateApprovalTokenAsync(ApprovalToken tokenString, CancellationToken cancellationToken = default)
    {
        _context.ApprovalTokens.Update(tokenString);
        return Task.CompletedTask;
    }
}
