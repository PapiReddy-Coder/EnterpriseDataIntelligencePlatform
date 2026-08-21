
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Domain;

namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface IJwtTokenService
{
    Task<TokenResponse> CreateSessionAsync(AppUser user, CancellationToken cancellationToken = default);
    Task<TokenResponse?> RotateAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
    Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default);
}
