
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class AuthService(
    UserManager<AppUser> userManager,
    IJwtTokenService tokenService,
    AppDbContext dbContext,
    IAuditService auditService,
    IEmailSender emailSender,
    IConfiguration configuration) : IAuthService
{
    public async Task<TokenResponse?> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return null;
        }

        if (user.WorkspaceId is Guid workspaceId &&
            !await dbContext.Workspaces.AnyAsync(x => x.Id == workspaceId && x.IsActive, cancellationToken))
        {
            return null;
        }

        var response = await tokenService.CreateSessionAsync(user, cancellationToken);
        await auditService.WriteAsync(
            "Login",
            "Authentication",
            user.Id.ToString(),
            ipAddress: ipAddress,
            userId: user.Id,
            workspaceId: user.WorkspaceId,
            cancellationToken: cancellationToken);

        return response;
    }

    public async Task<bool> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return false;
        }

        await userManager.UpdateSecurityStampAsync(user);
        await tokenService.RevokeAllAsync(user.Id, cancellationToken);
        await auditService.WriteAsync(
            "PasswordChanged",
            "User",
            user.Id.ToString(),
            cancellationToken: cancellationToken);

        return true;
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim();
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is null || !user.IsActive)
        {
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var clientBaseUrl = configuration["PasswordReset:ClientBaseUrl"]
            ?? throw new InvalidOperationException("Password reset client URL is not configured.");

        var resetUrl = $"{clientBaseUrl}?email={Uri.EscapeDataString(normalizedEmail)}&token={Uri.EscapeDataString(token)}";
        await emailSender.SendAsync(
            normalizedEmail,
            "EDIP password reset",
            $"Reset your password using this time-limited link: {resetUrl}",
            cancellationToken);
    }

    public async Task<bool> ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return false;
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return false;
        }

        await userManager.UpdateSecurityStampAsync(user);
        await tokenService.RevokeAllAsync(user.Id, cancellationToken);
        await auditService.WriteAsync(
            "PasswordReset",
            "User",
            user.Id.ToString(),
            cancellationToken: cancellationToken);

        return true;
    }
}
