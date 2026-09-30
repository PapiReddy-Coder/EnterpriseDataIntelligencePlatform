using EnterpriseDataIntelligencePlatform.Domain;

namespace EnterpriseDataIntelligencePlatform.Data.Seed;

public static class PermissionSeed
{
    public static readonly Permission[] Data =
    [
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Name = Permissions.WorkspacesManage,
            Description = "Manage all workspaces"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
            Name = Permissions.UsersManageAll,
            Description = "Manage all platform users"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
            Name = Permissions.UsersManageWorkspace,
            Description = "Manage users within a workspace"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000004"),
            Name = Permissions.RolesAssignAll,
            Description = "Assign all platform and workspace roles"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000005"),
            Name = Permissions.RolesAssignWorkspace,
            Description = "Assign workspace-specific roles"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000006"),
            Name = Permissions.PlatformViewAll,
            Description = "View all platform data"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000007"),
            Name = Permissions.PlatformConfigure,
            Description = "Configure platform settings"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000008"),
            Name = Permissions.WorkspaceConfigure,
            Description = "Configure workspace settings"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000009"),
            Name = Permissions.AnalyticsView,
            Description = "View workspace analytics and reports"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000010"),
            Name = Permissions.DatasetsManage,
            Description = "Manage datasets"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000011"),
            Name = Permissions.DashboardsConfigure,
            Description = "Configure dashboards"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000012"),
            Name = Permissions.ReportsModify,
            Description = "Create and modify reports"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000013"),
            Name = Permissions.AnalyticsExecute,
            Description = "Execute analytics queries"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000014"),
            Name = Permissions.DashboardsView,
            Description = "View dashboards"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000015"),
            Name = Permissions.ReportsGenerate,
            Description = "Generate reports"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000016"),
            Name = Permissions.InsightsAccess,
            Description = "Access business insights"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000017"),
            Name = Permissions.DataRequestsSubmit,
            Description = "Submit data requests"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000018"),
            Name = Permissions.ReportsRead,
            Description = "Read-only access to reports"
        }
        ,new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000019"),
            Name = Permissions.DatasetsView,
            Description = "View dataset catalog and details"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000020"),
            Name = Permissions.DatasetsCreate,
            Description = "Register datasets"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000021"),
            Name = Permissions.DatasetsUpdate,
            Description = "Update dataset metadata"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000022"),
            Name = Permissions.DatasetsArchive,
            Description = "Archive datasets"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000023"),
            Name = Permissions.DatasetsRestore,
            Description = "Restore archived or soft-deleted datasets"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000024"),
            Name = Permissions.DatasetsDelete,
            Description = "Soft delete datasets"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000025"),
            Name = Permissions.DatasetVersionsView,
            Description = "View dataset version history"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000026"),
            Name = Permissions.DatasetVersionsRestore,
            Description = "Restore previous dataset metadata versions"
        },
        new Permission
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000027"),
            Name = Permissions.DatasetCategoriesManage,
            Description = "Manage dataset category master data"
        }

        ,new Permission
        {
            Id = SeedIdFactory.Permission(28),
            Name = Permissions.ImportsUpload,
            Description = "Upload import files"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(29),
            Name = Permissions.ImportsCreate,
            Description = "Create dataset imports"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(30),
            Name = Permissions.ImportsStart,
            Description = "Start dataset imports"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(31),
            Name = Permissions.ImportsView,
            Description = "View import status and history"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(32),
            Name = Permissions.ImportsErrorsView,
            Description = "View import row errors"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(33),
            Name = Permissions.ImportsCancel,
            Description = "Cancel active imports"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(34),
            Name = Permissions.ImportsSchemaManage,
            Description = "Manage dataset ingestion key columns"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(35),
            Name = Permissions.QualityProfilesRun,
            Description = "Run data quality profiles"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(36),
            Name = Permissions.QualityProfilesView,
            Description = "View data quality profiles and dashboard metrics"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(37),
            Name = Permissions.QualityIssuesView,
            Description = "View detailed data quality issues and problematic records"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(38),
            Name = Permissions.QualityThresholdsManage,
            Description = "Manage dataset-level data quality thresholds"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(39),
            Name = Permissions.ReportsExport,
            Description = "Export permitted analytics reports"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(40),
            Name = Permissions.LineageView,
            Description = "View permitted lineage graphs and relationships"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(41),
            Name = Permissions.LineageManage,
            Description = "Manage lineage relationships within the permitted workspace"
        }
        ,new Permission
        {
            Id = SeedIdFactory.Permission(42),
            Name = Permissions.LineageImpact,
            Description = "Run downstream lineage impact analysis"
        }
        ,new Permission { Id = SeedIdFactory.Permission(43), Name = Permissions.GovernanceView, Description = "View dataset governance metadata and history" }
        ,new Permission { Id = SeedIdFactory.Permission(44), Name = Permissions.GovernanceManage, Description = "Manage dataset governance metadata" }
        ,new Permission { Id = SeedIdFactory.Permission(45), Name = Permissions.GovernanceCertify, Description = "Review and certify governed datasets" }
        ,new Permission { Id = SeedIdFactory.Permission(46), Name = Permissions.DatasetAccessRequest, Description = "Request dataset access and view permitted requests" }
        ,new Permission { Id = SeedIdFactory.Permission(47), Name = Permissions.DatasetAccessReview, Description = "Approve or reject dataset access requests" }
        ,new Permission { Id = SeedIdFactory.Permission(48), Name = Permissions.DatasetAccessRevoke, Description = "Revoke dataset access grants" }
        ,new Permission { Id = SeedIdFactory.Permission(49), Name = Permissions.GovernanceDashboard, Description = "View governance dashboard data" }
    ];
}
