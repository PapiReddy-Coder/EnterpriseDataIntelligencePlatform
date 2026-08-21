
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController, Route("api/auth")]

public sealed class AuthController(IAuthService auth, IJwtTokenService tokens, ICurrentUser current, IAuditService audit) : ControllerBase
{
    [AllowAnonymous, HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest r)
    {
        var t = await auth.LoginAsync(r, HttpContext.Connection.RemoteIpAddress?.ToString());
        return t is null ? Unauthorized() : Ok(t);
    }
    [AllowAnonymous, HttpPost("refresh")] 
    public async Task<IActionResult> Refresh(RefreshRequest r) 
    {
        var t = await tokens.RotateAsync(r.RefreshToken); 
        return t is null ? Unauthorized() : Ok(t); 
    }
    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout() 
    {
        if (current.UserId is null || current.SessionId is null) 
            return Unauthorized(); 
        await tokens.RevokeSessionAsync(current.UserId.Value, 
            current.SessionId.Value); 
        await audit.WriteAsync("Logout", "Authentication"); 
        return NoContent(); 
    }
    [Authorize, HttpPost("change-password")] 
    public async Task<IActionResult> Change(ChangePasswordRequest r) => await auth.ChangePasswordAsync(current.UserId!.Value, r) ? NoContent() : BadRequest();
    [AllowAnonymous, HttpPost("forgot-password")] 
    public async Task<IActionResult> Forgot(ForgotPasswordRequest r) 
    {
        await auth.ForgotPasswordAsync(r.Email); 
        return Accepted(); 
    }
    [AllowAnonymous, HttpPost("reset-password")] 
    public async Task<IActionResult> Reset(ResetPasswordRequest r) => await auth.ResetPasswordAsync(r) ? NoContent() : BadRequest();
}
