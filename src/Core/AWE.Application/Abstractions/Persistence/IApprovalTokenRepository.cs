using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.Persistence;

public interface IApprovalTokenRepository
{
    //public Task<ApprovalToken?> GetTokenByIdAsync(Guid tokenId, CancellationToken cancellationToken = default);
    //public Task<ApprovalToken?> GetTokenByIdAsync(string tokenId, CancellationToken cancellationToken = default);
    public Task<ApprovalToken?> GetByTokenStringAsync(string token, CancellationToken cancellationToken = default);
    public void DeleteTokenAsync(Guid tokenId, CancellationToken cancellationToken = default);
    public Task CreateToken(ApprovalToken token, CancellationToken cancellationToken = default);
    public Task<ApprovalToken?> GetByPointerIdAsync(Guid id, CancellationToken cancellationToken = default);
    public Task UpdateApprovalTokenAsync(ApprovalToken tokenString, CancellationToken cancellationToken = default);
}
