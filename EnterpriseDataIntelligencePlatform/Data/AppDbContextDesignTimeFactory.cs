using EnterpriseDataIntelligencePlatform.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnterpriseDataIntelligencePlatform.Data;

/// <summary>Scaffolding migrations must not execute Program's startup migrations or user seed.</summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("DefaultConnection") ??
            "Server=(localdb)\\MSSQLLocalDB;Database=EnterpriseDataIntelligencePlatform;Trusted_Connection=True;TrustServerCertificate=True";
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options, new DesignUser());
    }
    private sealed class DesignUser : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? WorkspaceId => null;
        public Guid? SessionId => null;
        public bool IsPlatformAdministrator => true;
    }
}
