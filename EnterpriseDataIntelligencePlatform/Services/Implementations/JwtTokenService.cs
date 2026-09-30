
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class JwtTokenService(
    AppDbContext dbContext,
    UserManager<AppUser> userManager,
    IConfiguration configuration) : IJwtTokenService
{
    public async Task<TokenResponse> CreateSessionAsync(
        AppUser user,
        CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();
        var rawRefreshToken = GenerateRefreshToken();
        var accessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(GetAccessTokenMinutes());
        var refreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(GetRefreshTokenDays());

        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            SessionId = sessionId,
            TokenHash = HashToken(rawRefreshToken),
            ExpiresAtUtc = refreshTokenExpiresAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = await GenerateAccessTokenAsync(user, sessionId, accessTokenExpiresAtUtc);
        return new TokenResponse(accessToken, rawRefreshToken, accessTokenExpiresAtUtc, refreshTokenExpiresAtUtc);
    }

    public async Task<TokenResponse?> RotateAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var tokenHash = HashToken(refreshToken);
        var existingToken = await dbContext.RefreshTokens
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (existingToken is null || !existingToken.IsActive || !existingToken.User.IsActive)
        {
            return null;
        }

        if (existingToken.User.WorkspaceId is Guid workspaceId &&
            !await dbContext.Workspaces.AnyAsync(x => x.Id == workspaceId && x.IsActive, cancellationToken))
        {
            return null;
        }

        existingToken.RevokedAtUtc = DateTime.UtcNow;

        var newRawRefreshToken = GenerateRefreshToken();
        var newTokenHash = HashToken(newRawRefreshToken);
        var refreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(GetRefreshTokenDays());
        existingToken.ReplacedByTokenHash = newTokenHash;

        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = existingToken.UserId,
            SessionId = existingToken.SessionId,
            TokenHash = newTokenHash,
            ExpiresAtUtc = refreshTokenExpiresAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var accessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(GetAccessTokenMinutes());
        var accessToken = await GenerateAccessTokenAsync(
            existingToken.User,
            existingToken.SessionId,
            accessTokenExpiresAtUtc);

        return new TokenResponse(
            accessToken,
            newRawRefreshToken,
            accessTokenExpiresAtUtc,
            refreshTokenExpiresAtUtc);
    }

    public async Task RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var tokens = await dbContext.RefreshTokens
            .Where(x => x.UserId == userId && x.SessionId == sessionId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var tokens = await dbContext.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> GenerateAccessTokenAsync(AppUser user, Guid sessionId, DateTime expiresAtUtc)
    {
        var roles = await userManager.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new("session_id", sessionId.ToString()),
            new("security_stamp", user.SecurityStamp ?? string.Empty)
        };

        if (user.WorkspaceId is Guid workspaceId)
        {
            claims.Add(new Claim("workspace_id", workspaceId.ToString()));
        }

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var jwtKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT signing key is not configured.");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private int GetAccessTokenMinutes() => configuration.GetValue("Jwt:AccessTokenMinutes", 30);
    private int GetRefreshTokenDays() => configuration.GetValue("Jwt:RefreshTokenDays", 7);
    private static string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
