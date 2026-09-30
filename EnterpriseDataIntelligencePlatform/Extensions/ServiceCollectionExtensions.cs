
using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Background;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Implementations;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using EnterpriseDataIntelligencePlatform.Services.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseDataIntelligencePlatform.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sqlOptions =>
                {
                    sqlOptions.CommandTimeout(120);
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorNumbersToAdd: null);
                }));

        services.AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
                options.User.RequireUniqueEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromMinutes(
                configuration.GetValue("PasswordReset:ExpiryMinutes", 30)));

        services.AddJwtAuthentication(configuration);
        services.AddApplicationAuthorization();

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDatasetService, DatasetService>();
        services.AddScoped<IDatasetAccessPolicy, DatasetAccessPolicy>();
        services.AddScoped<IGovernanceService, GovernanceService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddMemoryCache();
        services.Configure<AnalyticsOptions>(configuration.GetSection("Analytics"));
        services.AddScoped<AnalyticsQueries>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<IAnalyticsService>(sp => sp.GetRequiredService<AnalyticsService>());
        services.AddScoped<IReportService, ReportService>();
        services.AddSingleton<IReportExporter, ReportExporter>();
        services.Configure<LineageOptions>(configuration.GetSection("Lineage"));
        services.AddScoped<ILineageService, LineageService>();
        services.AddScoped<ILineageCaptureService, LineageCaptureService>();
        services.AddScoped<IEmailSender, EmailSender>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IImportProcessor, ImportProcessor>();
        services.AddScoped<ITransformationService, TransformationService>();
        services.AddScoped<IDataQualityProfileService, DataQualityProfileService>();
        services.AddScoped<IDataQualityProfileProcessor, DataQualityProfileProcessor>();
        services.AddSingleton<ITransformationEngine, TransformationEngine>();
        services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IDataSharingService, DataSharingService>();
        services.AddSingleton<IExportStorageService, LocalExportStorageService>();
        services.AddSingleton<IImportFileReader, ImportFileReader>();
        services.AddSingleton<IBackgroundJobQueue, BackgroundJobQueue>();
        services.AddSingleton<IImportCancellationRegistry, ImportCancellationRegistry>();
        services.AddSingleton<IQualityProfileCancellationRegistry, QualityProfileCancellationRegistry>();
        services.AddHostedService<BackgroundJobService>();
        services.AddHostedService<ExportCleanupBackgroundService>();

        services.AddControllers();
        services.Configure<ApiBehaviorOptions>(options =>
        {
            var originalFactory = options.InvalidModelStateResponseFactory;
            options.InvalidModelStateResponseFactory = context =>
                AnalyticsResponses.IsAnalytics(context.HttpContext.Request.Path)
                    ? new BadRequestObjectResult(AnalyticsResponses.Failure(context.HttpContext, "InvalidParameters",
                        "One or more parameters are invalid.", context.ModelState
                            .Where(x => x.Value?.Errors.Count > 0)
                            .Select(x => $"Invalid parameter: {x.Key}.").ToList()))
                    : originalFactory(context);
        });
        services.AddEndpointsApiExplorer();
        services.AddApplicationSwagger();

        return services;
    }

    private static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var key = jwtSection["Key"]
            ?? throw new InvalidOperationException("JWT signing key is not configured.");

        if (Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("JWT signing key must be at least 32 bytes.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtSection["Issuer"],
                    ValidateAudience = true,
                    ValidAudience = jwtSection["Audience"],
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = TokenValidationEvents.ValidateAsync
                };
            });

        return services;
    }

    private static IServiceCollection AddApplicationAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AnalyticsPolicies.Read, policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new AnalyticsPermissionRequirement(false)));
            options.AddPolicy(AnalyticsPolicies.Export, policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new AnalyticsPermissionRequirement(true)));
        });
        services.AddScoped<IAuthorizationHandler, AnalyticsPermissionHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }

    private static IServiceCollection AddApplicationSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.OperationFilter<AnalyticsOpenApiFilter>();
            var xml = Path.Combine(AppContext.BaseDirectory, "EnterpriseDataIntelligencePlatform.xml");
            if (File.Exists(xml)) options.IncludeXmlComments(xml);
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Enterprise Data Intelligence Platform API",
                Version = "v1",
                Description = "Authentication, datasets, ingestion, quality profiling, analytics, reporting, data lineage, governance, classification, certification, and dataset access approvals with RBAC and workspace isolation."
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter the JWT access token."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                }] = Array.Empty<string>()
            });
        });

        return services;
    }
}
