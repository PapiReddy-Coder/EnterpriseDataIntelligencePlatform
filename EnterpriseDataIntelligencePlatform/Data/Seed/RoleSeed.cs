using EnterpriseDataIntelligencePlatform.Domain;

namespace EnterpriseDataIntelligencePlatform.Data.Seed;

public static class RoleSeed
{
    public static readonly AppRole[] Data =
    [
        new AppRole
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = Roles.PlatformAdministrator,
            NormalizedName = Roles.PlatformAdministrator.ToUpperInvariant(),
            ConcurrencyStamp = "11111111-aaaa-aaaa-aaaa-111111111111",
            IsGlobal = true,
            Description = "Predefined Platform Administrator role"
        },
        new AppRole
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = Roles.WorkspaceAdministrator,
            NormalizedName = Roles.WorkspaceAdministrator.ToUpperInvariant(),
            ConcurrencyStamp = "22222222-aaaa-aaaa-aaaa-222222222222",
            IsGlobal = false,
            Description = "Predefined Workspace Administrator role"
        },
        new AppRole
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = Roles.DataAnalyst,
            NormalizedName = Roles.DataAnalyst.ToUpperInvariant(),
            ConcurrencyStamp = "33333333-aaaa-aaaa-aaaa-333333333333",
            IsGlobal = false,
            Description = "Predefined Data Analyst role"
        },
        new AppRole
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Name = Roles.BusinessUser,
            NormalizedName = Roles.BusinessUser.ToUpperInvariant(),
            ConcurrencyStamp = "44444444-aaaa-aaaa-aaaa-444444444444",
            IsGlobal = false,
            Description = "Predefined Business User role"
        },
        new AppRole
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Name = Roles.Viewer,
            NormalizedName = Roles.Viewer.ToUpperInvariant(),
            ConcurrencyStamp = "55555555-aaaa-aaaa-aaaa-555555555555",
            IsGlobal = false,
            Description = "Predefined Viewer role"
        }
    ];
}
