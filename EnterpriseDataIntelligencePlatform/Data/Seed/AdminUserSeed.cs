using EnterpriseDataIntelligencePlatform.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace EnterpriseDataIntelligencePlatform.Data.Seed;

public static class AdminUserSeed
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var email = configuration["DefaultAdmin:Email"];
        var password = configuration["DefaultAdmin:Password"];
        var fullName = configuration["DefaultAdmin:FullName"];

        var roleName = Roles.PlatformAdministrator;

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(fullName))
        {
            throw new InvalidOperationException(
                "DefaultAdmin configuration is missing in appsettings.json.");
        }

        var existingUser = await userManager.FindByEmailAsync(email);

        if (existingUser != null)
        {
            return;
        }

        var adminUser = new AppUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            EmailConfirmed = true,
            IsActive = true,
            WorkspaceId = null
        };

        var createResult = await userManager.CreateAsync(adminUser, password);

        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ",
                createResult.Errors.Select(e => e.Description));

            throw new InvalidOperationException(
                $"Admin user creation failed: {errors}");
        }

        var roleResult = await userManager.AddToRoleAsync(adminUser, roleName);

        if (!roleResult.Succeeded)
        {
            var errors = string.Join(", ",
                roleResult.Errors.Select(e => e.Description));

            throw new InvalidOperationException(
                $"Admin role assignment failed: {errors}");
        }
    }
}