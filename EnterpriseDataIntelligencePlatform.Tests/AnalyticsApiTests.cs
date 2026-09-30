using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using EnterpriseDataIntelligencePlatform.Controllers;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Data.Analytics;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Extensions;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseDataIntelligencePlatform.Tests;

/// <summary>Real HTTP/controller/model-binding/policy tests against Kestrel.
/// Only authentication and SQL view storage are substituted; production policies are used.</summary>
public sealed class AnalyticsApiTests
{
    [Fact]
    public async Task UnauthenticatedRequestsUseNew401Envelope()
    {
        await using var host = await ApiHost.CreateAsync();
        var response = await host.Client.GetAsync("/api/analytics/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("Unauthenticated", json.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(Roles.PlatformAdministrator, 200)]
    [InlineData(Roles.WorkspaceAdministrator, 200)]
    [InlineData(Roles.DataAnalyst, 200)]
    [InlineData(Roles.BusinessUser, 403)]
    [InlineData(Roles.Viewer, 403)]
    public async Task ExportAuthorizationAndSummaryAccessAreEnforced(string role, int status)
    {
        await using var host = await ApiHost.CreateAsync();
        host.SignIn(role);
        var summary = await host.Client.GetAsync("/api/analytics/summary");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        var export = await host.Client.GetAsync("/api/reports/dataset-summary/export?Format=csv");
        Assert.Equal(status, (int)export.StatusCode);
        if (status == 200)
        {
            Assert.Equal("text/csv", export.Content.Headers.ContentType!.MediaType);
            Assert.Equal("1", export.Headers.GetValues("X-Report-Row-Count").Single());
        }
        else
        {
            var json = await export.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(json.GetProperty("success").GetBoolean());
        }
    }

    [Theory]
    [InlineData("/api/analytics/datasets?PageSize=101")]
    [InlineData("/api/analytics/datasets?PageSize=abc")]
    [InlineData("/api/analytics/datasets?WorkspaceId=not-a-guid")]
    [InlineData("/api/analytics/datasets?SortBy=unknown")]
    [InlineData("/api/analytics/imports?DateField=updated")]
    [InlineData("/api/analytics/quality/trends?Grouping=hourly")]
    [InlineData("/api/reports/dataset-summary/export?Format=pdf")]
    public async Task InvalidInputsUse400Envelope(string path)
    {
        await using var host = await ApiHost.CreateAsync();
        host.SignIn(Roles.DataAnalyst);
        var response = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.True(json.TryGetProperty("metadata", out _));
    }

    [Fact]
    public async Task ForeignWorkspaceIsDeniedInSummaryReportsAndExports()
    {
        await using var host = await ApiHost.CreateAsync();
        host.SignIn(Roles.WorkspaceAdministrator);
        foreach (var path in new[] { "/api/analytics/summary", "/api/reports/dataset-summary",
                     "/api/reports/dataset-summary/export" })
        {
            var response = await host.Client.GetAsync($"{path}?WorkspaceId={Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task NewEnvelopeDoesNotChangeOldValidationResponses()
    {
        await using var host = await ApiHost.CreateAsync();
        host.SignIn(Roles.PlatformAdministrator);
        var oldResponse = await host.Client.GetAsync("/api/datasets?PageSize=not-a-number");
        Assert.Equal(HttpStatusCode.BadRequest, oldResponse.StatusCode);
        var json = await oldResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.TryGetProperty("success", out _));
        Assert.True(json.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task EmptyResultIs200AndPaginationAndAuditArePresent()
    {
        await using var host = await ApiHost.CreateAsync();
        host.SignIn(Roles.DataAnalyst);
        var response = await host.Client.GetAsync("/api/reports/dataset-summary?Search=absent");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(json.GetProperty("data").GetProperty("rows").EnumerateArray());
        Assert.Equal(0, json.GetProperty("pagination").GetProperty("totalCount").GetInt64());
        using var scope = host.App.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs.AnyAsync(x => x.Action == "Report Generated"));
    }

    [Fact]
    public async Task UnknownAnalyticsRouteUses404Envelope()
    {
        await using var host = await ApiHost.CreateAsync();
        host.SignIn(Roles.Viewer);
        var response = await host.Client.GetAsync("/api/analytics/unknown");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task SwaggerListsNewAndExistingEndpoints()
    {
        await using var host = await ApiHost.CreateAsync();
        var response = await host.Client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paths = document.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/analytics/summary", out _));
        Assert.True(paths.TryGetProperty("/api/reports/{reportType}/export", out _));
        Assert.True(paths.TryGetProperty("/api/lineage/datasets/{datasetId}", out _));
        Assert.True(paths.TryGetProperty("/api/lineage/datasets/{datasetId}/impact", out _));
        Assert.True(paths.TryGetProperty("/api/datasets", out _));
    }

    [Fact]
    public async Task LineageEndpointsEnforceAuthenticationPermissionsAndStandardEnvelope()
    {
        await using var host = await ApiHost.CreateAsync();
        var anonymous = await host.Client.GetAsync($"/api/lineage/datasets/{host.DatasetAId}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.False((await anonymous.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());

        host.SignIn(Roles.Viewer);
        Assert.Equal(HttpStatusCode.OK,
            (await host.Client.GetAsync($"/api/lineage/datasets/{host.DatasetAId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await host.Client.GetAsync($"/api/lineage/datasets/{host.DatasetAId}/impact")).StatusCode);

        host.SignIn(Roles.DataAnalyst);
        Assert.Equal(HttpStatusCode.OK,
            (await host.Client.GetAsync($"/api/lineage/datasets/{host.DatasetAId}/impact")).StatusCode);
        var analystCreate = await host.Client.PostAsJsonAsync("/api/lineage/relationships", new
        {
            sourceEntityType = "Dataset", sourceEntityId = host.DatasetAId,
            targetEntityType = "Dataset", targetEntityId = host.DatasetBId,
            relationshipType = "Feeds", metadata = new { purpose = "HTTP integration" }
        });
        Assert.Equal(HttpStatusCode.Forbidden, analystCreate.StatusCode);

        host.SignIn(Roles.WorkspaceAdministrator);
        var created = await host.Client.PostAsJsonAsync("/api/lineage/relationships", new
        {
            sourceEntityType = "Dataset", sourceEntityId = host.DatasetAId,
            targetEntityType = "Dataset", targetEntityId = host.DatasetBId,
            relationshipType = "Feeds", metadata = new { purpose = "HTTP integration" }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var json = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal("Feeds", json.GetProperty("data").GetProperty("relationshipType").GetString());
    }

    private sealed class ApiHost : IAsyncDisposable
    {
        public WebApplication App { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        private readonly Dictionary<string, Guid> users = [];
        public Guid DatasetAId { get; } = Guid.NewGuid();
        public Guid DatasetBId { get; } = Guid.NewGuid();

        public static async Task<ApiHost> CreateAsync()
        {
            var host = new ApiHost();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=UnusedByHttpTests;Integrated Security=true",
                ["Jwt:Key"] = "Task20-Test-Only-Key-Not-For-Production-12345",
                ["Jwt:Issuer"] = "Tests", ["Jwt:Audience"] = "Tests"
            });
            builder.Services.AddApplicationServices(builder.Configuration);
            builder.Services.RemoveAll<AppDbContext>();
            builder.Services.RemoveAll<DbContextOptions<AppDbContext>>();
            builder.Services.RemoveAll<ICurrentUser>();
            // Remove provider options and hosted jobs for the isolated in-memory test host.
            builder.Services.RemoveAll<DbContextOptions>();
            builder.Services.RemoveAll<IHostedService>();
            var name = Guid.NewGuid().ToString();
            builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(name));
            builder.Services.AddScoped<ICurrentUser, TestCurrentUserContext>();
            builder.Services.AddControllers().AddApplicationPart(typeof(AnalyticsController).Assembly);
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "TestOnly"; options.DefaultChallengeScheme = "TestOnly";
                options.DefaultForbidScheme = "TestOnly";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("TestOnly", _ => { });
            host.App = builder.Build();
            host.App.UseMiddleware<AnalyticsResponseMiddleware>();
            host.App.UseSwagger();
            host.App.UseAuthentication();
            host.App.UseAuthorization();
            host.App.MapControllers();
            using (var scope = host.App.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.EnsureCreatedAsync();
                var workspace = Guid.NewGuid();
                db.Workspaces.Add(new Workspace { Id = workspace, Name = "Test workspace", Code = "HTTP" });
                db.DatasetAnalytics.Add(new DatasetAnalyticsRow { DatasetId = Guid.NewGuid(), WorkspaceId = workspace,
                    Name = "HTTP Dataset", Code = "HTTP-1", Status = "Active", NoCompletedProfile = true, RequiresAttention = true });
                foreach (var role in await db.Roles.ToListAsync())
                {
                    var id = Guid.NewGuid(); host.users[role.Name!] = id;
                    db.Users.Add(new AppUser { Id = id, WorkspaceId = workspace, FullName = role.Name!, UserName = id.ToString() });
                    db.UserRoles.Add(new() { RoleId = role.Id, UserId = id });
                }
                var ownerId = host.users[Roles.WorkspaceAdministrator];
                var categoryId = (await db.DatasetCategories.FirstAsync()).Id;
                foreach (var pair in new[] { (host.DatasetAId, "HTTP-A"), (host.DatasetBId, "HTTP-B") })
                {
                    db.Datasets.Add(new Dataset
                    {
                        Id = pair.Item1, WorkspaceId = workspace, OwnerId = ownerId, CategoryId = categoryId,
                        Code = pair.Item2, Name = pair.Item2, NormalizedName = pair.Item2,
                        DataSourceName = "HTTP source", DataSourceType = "Test", Status = DatasetStatuses.Active
                    });
                    db.DatasetVersions.Add(new DatasetVersion
                    {
                        WorkspaceId = workspace, DatasetId = pair.Item1, VersionNumber = 1, IsCurrent = true,
                        Code = pair.Item2, Name = pair.Item2, CategoryId = categoryId, CategoryName = "Test",
                        OwnerId = ownerId, OwnerName = "Workspace Administrator", DataSourceName = "HTTP source",
                        DataSourceType = "Test", Status = DatasetStatuses.Active, CreatedByUserId = ownerId
                    });
                }
                await db.SaveChangesAsync();
            }
            await host.App.StartAsync();
            var address = host.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            host.Client = new HttpClient { BaseAddress = new Uri(address) };
            return host;
        }

        public void SignIn(string role)
        {
            Client.DefaultRequestHeaders.Remove("X-Test-User");
            Client.DefaultRequestHeaders.Add("X-Test-User", users[role].ToString());
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await App.StopAsync(); await App.DisposeAsync(); }
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, AppDbContext db)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Test-User"], out var id)) return AuthenticateResult.NoResult();
            var user = await db.Users.SingleAsync(x => x.Id == id);
            var roleNames = await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id
                                  where ur.UserId == id select role.Name!).ToListAsync();
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id.ToString()), new("workspace_id", user.WorkspaceId.ToString()!) };
            claims.AddRange(roleNames.Select(x => new Claim(ClaimTypes.Role, x)));
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
        }
    }

    private sealed class TestCurrentUserContext(IHttpContextAccessor accessor) : ICurrentUser
    {
        private ClaimsPrincipal? User => accessor.HttpContext?.User;
        private Guid? Parse(string claim) => Guid.TryParse(User?.FindFirstValue(claim), out var value) ? value : null;
        public Guid? UserId => Parse(ClaimTypes.NameIdentifier);
        public Guid? WorkspaceId => Parse("workspace_id");
        public Guid? SessionId => null;
        // Test fixture seeding has no HTTP context; request-time behavior still follows the authenticated role.
        public bool IsPlatformAdministrator => accessor.HttpContext is null ||
            User?.IsInRole(Roles.PlatformAdministrator) == true;
    }
}
