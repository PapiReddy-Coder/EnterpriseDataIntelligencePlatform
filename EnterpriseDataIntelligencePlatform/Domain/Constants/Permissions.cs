namespace EnterpriseDataIntelligencePlatform.Domain;

public static class Permissions
{
    public const string WorkspacesManage = "workspaces.manage";
    public const string UsersManageAll = "users.manage.all";
    public const string UsersManageWorkspace = "users.manage.workspace";
    public const string RolesAssignAll = "roles.assign.all";
    public const string RolesAssignWorkspace = "roles.assign.workspace";
    public const string PlatformViewAll = "platform.view.all";
    public const string PlatformConfigure = "platform.configure";
    public const string WorkspaceConfigure = "workspace.configure";
    public const string AnalyticsView = "analytics.view";
    public const string DatasetsManage = "datasets.manage";
    public const string DashboardsConfigure = "dashboards.configure";
    public const string ReportsModify = "reports.modify";
    public const string AnalyticsExecute = "analytics.execute";
    public const string DashboardsView = "dashboards.view";
    public const string ReportsGenerate = "reports.generate";
    public const string InsightsAccess = "insights.access";
    public const string DataRequestsSubmit = "datarequests.submit";
    public const string ReportsRead = "reports.read";

    public const string DatasetsView = "datasets.view";
    public const string DatasetsCreate = "datasets.create";
    public const string DatasetsUpdate = "datasets.update";
    public const string DatasetsArchive = "datasets.archive";
    public const string DatasetsRestore = "datasets.restore";
    public const string DatasetsDelete = "datasets.delete";
    public const string DatasetVersionsView = "datasets.versions.view";
    public const string DatasetVersionsRestore = "datasets.versions.restore";
    public const string DatasetCategoriesManage = "datasets.categories.manage";
    public const string ImportsUpload = "imports.upload";
    public const string ImportsCreate = "imports.create";
    public const string ImportsStart = "imports.start";
    public const string ImportsView = "imports.view";
    public const string ImportsErrorsView = "imports.errors.view";
    public const string ImportsCancel = "imports.cancel";
    public const string ImportsSchemaManage = "imports.schema.manage";


    public static readonly string[] All =
    [
        WorkspacesManage, UsersManageAll, UsersManageWorkspace,
        RolesAssignAll, RolesAssignWorkspace, PlatformViewAll,
        PlatformConfigure, WorkspaceConfigure, AnalyticsView,
        DatasetsManage, DashboardsConfigure, ReportsModify,
        AnalyticsExecute, DashboardsView, ReportsGenerate,
        InsightsAccess, DataRequestsSubmit, ReportsRead,
        DatasetsView, DatasetsCreate, DatasetsUpdate,
        DatasetsArchive, DatasetsRestore, DatasetsDelete,
        DatasetVersionsView, DatasetVersionsRestore,
        DatasetCategoriesManage, ImportsUpload, ImportsCreate, ImportsStart, ImportsView,
        ImportsErrorsView, ImportsCancel, ImportsSchemaManage
    ];
}
