using EnterpriseDataIntelligencePlatform.Data.Seed;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EnterpriseDataIntelligencePlatform.Data;

public sealed class AppDbContext : IdentityDbContext<AppUser, AppRole, Guid>
{
    private readonly ICurrentUser _currentUser;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

    public DbSet<Dataset> Datasets => Set<Dataset>();
    public DbSet<DatasetCategory> DatasetCategories => Set<DatasetCategory>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<DatasetTag> DatasetTags => Set<DatasetTag>();
    public DbSet<DatasetVersion> DatasetVersions => Set<DatasetVersion>();
    public DbSet<UploadedDataFile> UploadedDataFiles => Set<UploadedDataFile>();
    public DbSet<DatasetColumn> DatasetColumns => Set<DatasetColumn>();
    public DbSet<DataImport> DataImports => Set<DataImport>();
    public DbSet<ImportStagingRow> ImportStagingRows => Set<ImportStagingRow>();
    public DbSet<ImportStagingValue> ImportStagingValues => Set<ImportStagingValue>();
    public DbSet<DatasetRecord> DatasetRecords => Set<DatasetRecord>();
    public DbSet<DatasetRecordValue> DatasetRecordValues => Set<DatasetRecordValue>();
    public DbSet<ImportError> ImportErrors => Set<ImportError>();
    public DbSet<DatasetTransformationConfiguration> DatasetTransformationConfigurations => Set<DatasetTransformationConfiguration>();

    private Guid? CurrentWorkspaceId => _currentUser.WorkspaceId;
    private bool IsPlatformAdministrator => _currentUser.IsPlatformAdministrator;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasSequence<long>("DatasetCodeSequence", schema: "dbo")
            .StartsAt(1)
            .IncrementsBy(1);

        ConfigureWorkspace(builder);
        ConfigureApplicationUser(builder);
        ConfigurePermission(builder);
        ConfigureRolePermission(builder);
        ConfigureRefreshToken(builder);
        ConfigureAuditLog(builder);
        ConfigureLeaveRequest(builder);
        ConfigureDatasetCategory(builder);
        ConfigureDataset(builder);
        ConfigureTag(builder);
        ConfigureDatasetTag(builder);
        ConfigureDatasetVersion(builder);
        ConfigureIngestion(builder);
        ConfigureTransformation(builder);
        ConfigureSeedData(builder);
    }

    private void ConfigureTransformation(ModelBuilder builder)
    {
        builder.Entity<DatasetTransformationConfiguration>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.DatasetId, x.Version }).IsUnique();
            entity.HasIndex(x => new { x.DatasetId, x.IsActive }).HasFilter("[IsActive] = 1").IsUnique();
            entity.Property(x => x.ConfigurationJson).IsRequired().HasColumnType("nvarchar(max)");
            entity.HasOne(x => x.Dataset).WithMany().HasForeignKey(x => x.DatasetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x => IsPlatformAdministrator || (CurrentWorkspaceId != null && x.WorkspaceId == CurrentWorkspaceId));
        });

        builder.Entity<DataImport>()
            .HasOne(x => x.TransformationConfiguration)
            .WithMany()
            .HasForeignKey(x => x.TransformationConfigurationId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureWorkspace(ModelBuilder builder)
    {
        builder.Entity<Workspace>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).IsRequired().HasMaxLength(50);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(200);
        });
    }

    private static void ConfigureApplicationUser(ModelBuilder builder)
    {
        builder.Entity<AppUser>(entity =>
        {
            entity.HasOne(x => x.Workspace)
                .WithMany(x => x.Users)
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => x.WorkspaceId);
            entity.Property(x => x.FullName).IsRequired().HasMaxLength(200);
        });
    }

    private static void ConfigurePermission(ModelBuilder builder)
    {
        builder.Entity<Permission>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.Property(x => x.Name).IsRequired().HasMaxLength(150);
            entity.Property(x => x.Description).HasMaxLength(500);
        });
    }

    private static void ConfigureRolePermission(ModelBuilder builder)
    {
        builder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(x => new { x.RoleId, x.PermissionId });
            entity.HasOne(x => x.Role)
                .WithMany(x => x.RolePermissions)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Permission)
                .WithMany(x => x.RolePermissions)
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureRefreshToken(ModelBuilder builder)
    {
        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.SessionId });
            entity.Property(x => x.TokenHash).IsRequired().HasMaxLength(500);
            entity.HasOne(x => x.User)
                .WithMany(x => x.RefreshTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureAuditLog(ModelBuilder builder)
    {
        builder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.WorkspaceId);
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.Property(x => x.Action).IsRequired().HasMaxLength(150);
            entity.Property(x => x.EntityType).IsRequired().HasMaxLength(150);
            entity.Property(x => x.Details).HasMaxLength(4000);
        });
    }

    private void ConfigureLeaveRequest(ModelBuilder builder)
    {
        builder.Entity<LeaveRequest>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.WorkspaceId);
            entity.HasQueryFilter(x =>
                IsPlatformAdministrator ||
                (CurrentWorkspaceId != null &&
                 x.WorkspaceId == CurrentWorkspaceId));
        });
    }

    private static void ConfigureDatasetCategory(ModelBuilder builder)
    {
        builder.Entity<DatasetCategory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.NormalizedName).IsUnique();
            entity.Property(x => x.Name).IsRequired().HasMaxLength(100);
            entity.Property(x => x.NormalizedName).IsRequired().HasMaxLength(100);
            entity.Property(x => x.Description).HasMaxLength(500);
        });
    }

    private void ConfigureDataset(ModelBuilder builder)
    {
        builder.Entity<Dataset>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.WorkspaceId, x.NormalizedName })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(x => new { x.WorkspaceId, x.Status, x.UpdatedAtUtc });
            entity.HasIndex(x => x.CategoryId);
            entity.HasIndex(x => x.OwnerId);

            entity.Property(x => x.Code).IsRequired().HasMaxLength(20);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(200);
            entity.Property(x => x.NormalizedName).IsRequired().HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.DataSourceName).IsRequired().HasMaxLength(200);
            entity.Property(x => x.DataSourceType).IsRequired().HasMaxLength(100);
            entity.Property(x => x.DataSourceDescription).HasMaxLength(1000);
            entity.Property(x => x.Status).IsRequired().HasMaxLength(30);

            entity.HasOne(x => x.Workspace)
                .WithMany(x => x.Datasets)
                .HasForeignKey(x => x.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Category)
                .WithMany(x => x.Datasets)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Owner)
                .WithMany(x => x.OwnedDatasets)
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x =>
                !x.IsDeleted &&
                (IsPlatformAdministrator ||
                 (CurrentWorkspaceId != null &&
                  x.WorkspaceId == CurrentWorkspaceId)));
        });
    }

    private void ConfigureTag(ModelBuilder builder)
    {
        builder.Entity<Tag>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.WorkspaceId, x.NormalizedName }).IsUnique();
            entity.Property(x => x.Name).IsRequired().HasMaxLength(100);
            entity.Property(x => x.NormalizedName).IsRequired().HasMaxLength(100);
            entity.HasQueryFilter(x =>
                IsPlatformAdministrator ||
                (CurrentWorkspaceId != null &&
                 x.WorkspaceId == CurrentWorkspaceId));
        });
    }

    private void ConfigureDatasetTag(ModelBuilder builder)
    {
        builder.Entity<DatasetTag>(entity =>
        {
            entity.HasKey(x => new { x.DatasetId, x.TagId });
            entity.HasOne(x => x.Dataset)
                .WithMany(x => x.DatasetTags)
                .HasForeignKey(x => x.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Tag)
                .WithMany(x => x.DatasetTags)
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(x =>
                !x.Dataset.IsDeleted &&
                (IsPlatformAdministrator ||
                 (CurrentWorkspaceId != null &&
                  x.Dataset.WorkspaceId == CurrentWorkspaceId)));
        });
    }

    private void ConfigureDatasetVersion(ModelBuilder builder)
    {
        builder.Entity<DatasetVersion>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.DatasetId, x.VersionNumber }).IsUnique();
            entity.HasIndex(x => new { x.WorkspaceId, x.DatasetId });
            entity.Property(x => x.Code).IsRequired().HasMaxLength(20);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.CategoryName).IsRequired().HasMaxLength(100);
            entity.Property(x => x.OwnerName).IsRequired().HasMaxLength(200);
            entity.Property(x => x.DataSourceName).IsRequired().HasMaxLength(200);
            entity.Property(x => x.DataSourceType).IsRequired().HasMaxLength(100);
            entity.Property(x => x.DataSourceDescription).HasMaxLength(1000);
            entity.Property(x => x.Status).IsRequired().HasMaxLength(30);
            entity.Property(x => x.TagsJson).IsRequired().HasMaxLength(4000);
            entity.Property(x => x.VersionNotes).HasMaxLength(1000);
            entity.HasOne(x => x.Dataset)
                .WithMany(x => x.Versions)
                .HasForeignKey(x => x.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x =>
                IsPlatformAdministrator ||
                (CurrentWorkspaceId != null &&
                 x.WorkspaceId == CurrentWorkspaceId));
        });
    }


    private void ConfigureIngestion(ModelBuilder builder)
    {
        builder.Entity<UploadedDataFile>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.WorkspaceId,x.DatasetId}); e.Property(x=>x.OriginalFileName).HasMaxLength(255).IsRequired(); e.Property(x=>x.StoredFileName).HasMaxLength(255).IsRequired(); e.Property(x=>x.FilePath).HasMaxLength(1000).IsRequired(); e.Property(x=>x.Extension).HasMaxLength(10).IsRequired(); e.HasOne(x=>x.Dataset).WithMany().HasForeignKey(x=>x.DatasetId).OnDelete(DeleteBehavior.Restrict); e.HasQueryFilter(x=> IsPlatformAdministrator || (CurrentWorkspaceId!=null && x.WorkspaceId==CurrentWorkspaceId)); });
        builder.Entity<DatasetColumn>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.DatasetId,x.NormalizedName}).IsUnique(); e.Property(x=>x.Name).HasMaxLength(200).IsRequired(); e.Property(x=>x.NormalizedName).HasMaxLength(200).IsRequired(); e.Property(x=>x.DataType).HasMaxLength(30).IsRequired(); e.HasOne(x=>x.Dataset).WithMany().HasForeignKey(x=>x.DatasetId).OnDelete(DeleteBehavior.Cascade); e.HasQueryFilter(x=> IsPlatformAdministrator || (CurrentWorkspaceId!=null && x.WorkspaceId==CurrentWorkspaceId)); });
        builder.Entity<DataImport>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.WorkspaceId,x.DatasetId,x.CreatedAtUtc}); e.Property(x=>x.Status).HasMaxLength(50).IsRequired(); e.Property(x=>x.ImportMode).HasMaxLength(20).IsRequired(); e.Property(x=>x.DuplicateBehavior).HasMaxLength(20).IsRequired(); e.Property(x=>x.InvalidRecordBehavior).HasMaxLength(20).IsRequired(); e.Property(x=>x.CsvDelimiter).HasMaxLength(5).IsRequired(); e.Property(x=>x.WorksheetName).HasMaxLength(255); e.Property(x=>x.KeyColumnsJson).HasMaxLength(4000).IsRequired(); e.Property(x=>x.FailureMessage).HasMaxLength(4000); e.HasOne(x=>x.Dataset).WithMany().HasForeignKey(x=>x.DatasetId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x=>x.File).WithMany().HasForeignKey(x=>x.FileId).OnDelete(DeleteBehavior.Restrict); e.HasQueryFilter(x=> IsPlatformAdministrator || (CurrentWorkspaceId!=null && x.WorkspaceId==CurrentWorkspaceId)); });
        builder.Entity<ImportStagingRow>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.ImportId,x.RowNumber}); e.Property(x=>x.KeyHash).HasMaxLength(64).IsRequired(); e.HasOne(x=>x.Import).WithMany().HasForeignKey(x=>x.ImportId).OnDelete(DeleteBehavior.Cascade); e.HasQueryFilter(x=> IsPlatformAdministrator || (CurrentWorkspaceId!=null && x.WorkspaceId==CurrentWorkspaceId)); });
        builder.Entity<ImportStagingValue>(e => { e.HasKey(x=>x.Id); e.Property(x=>x.ColumnName).HasMaxLength(200).IsRequired(); e.Property(x=>x.RawValue).HasMaxLength(4000); e.Property(x=>x.OriginalValue).HasMaxLength(4000); e.Property(x=>x.TransformedValue).HasMaxLength(4000); e.HasOne(x=>x.StagingRow).WithMany(x=>x.Values).HasForeignKey(x=>x.StagingRowId).OnDelete(DeleteBehavior.Cascade); });
        builder.Entity<DatasetRecord>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.DatasetId,x.KeyHash}); e.Property(x=>x.KeyHash).HasMaxLength(64).IsRequired(); e.HasOne(x=>x.Dataset).WithMany().HasForeignKey(x=>x.DatasetId).OnDelete(DeleteBehavior.Cascade); e.HasQueryFilter(x=> IsPlatformAdministrator || (CurrentWorkspaceId!=null && x.WorkspaceId==CurrentWorkspaceId)); });
        builder.Entity<DatasetRecordValue>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.DatasetRecordId,x.DatasetColumnId}).IsUnique(); e.Property(x=>x.RawValue).HasMaxLength(4000); e.Property(x=>x.StringValue).HasMaxLength(4000); e.Property(x=>x.DecimalValue).HasPrecision(38,10); e.HasOne(x=>x.DatasetRecord).WithMany(x=>x.Values).HasForeignKey(x=>x.DatasetRecordId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x=>x.DatasetColumn).WithMany().HasForeignKey(x=>x.DatasetColumnId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<ImportError>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.ImportId,x.ErrorTimestampUtc}); e.Property(x=>x.ColumnName).HasMaxLength(200); e.Property(x=>x.InvalidValue).HasMaxLength(1000); e.Property(x=>x.ErrorType).HasMaxLength(50).IsRequired(); e.Property(x=>x.ValidationRule).HasMaxLength(100); e.Property(x=>x.ErrorDescription).HasMaxLength(2000).IsRequired(); e.HasOne(x=>x.Import).WithMany().HasForeignKey(x=>x.ImportId).OnDelete(DeleteBehavior.Cascade); e.HasQueryFilter(x=> IsPlatformAdministrator || (CurrentWorkspaceId!=null && x.WorkspaceId==CurrentWorkspaceId)); });
    }

    private static void ConfigureSeedData(ModelBuilder builder)
    {
        builder.Entity<AppRole>().HasData(RoleSeed.Data);
        builder.Entity<Permission>().HasData(PermissionSeed.Data);
        builder.Entity<RolePermission>().HasData(RolePermissionSeed.Data);
        builder.Entity<DatasetCategory>().HasData(DatasetCategorySeed.Data);
    }

    public override int SaveChanges()
    {
        ApplyWorkspaceRules();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyWorkspaceRules();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyWorkspaceRules()
    {
        var entries = ChangeTracker.Entries<IWorkspaceOwned>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified);

        foreach (var entry in entries)
        {
            ApplyWorkspaceRule(entry);
        }
    }

    private void ApplyWorkspaceRule(EntityEntry<IWorkspaceOwned> entry)
    {
        if (IsPlatformAdministrator)
        {
            if (entry.Entity.WorkspaceId == Guid.Empty)
            {
                throw new InvalidOperationException("A WorkspaceId must be provided for platform-level operations.");
            }
            return;
        }

        if (!CurrentWorkspaceId.HasValue)
        {
            throw new UnauthorizedAccessException("Workspace context is required.");
        }

        if (entry.State == EntityState.Modified)
        {
            var originalWorkspaceId = entry.Property(nameof(IWorkspaceOwned.WorkspaceId)).OriginalValue;
            if (!Equals(originalWorkspaceId, CurrentWorkspaceId.Value) ||
                entry.Entity.WorkspaceId != CurrentWorkspaceId.Value)
            {
                throw new UnauthorizedAccessException("A record cannot be moved to another workspace.");
            }
        }

        entry.Entity.WorkspaceId = CurrentWorkspaceId.Value;
    }
}
