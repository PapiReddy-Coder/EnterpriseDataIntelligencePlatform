namespace EnterpriseDataIntelligencePlatform.Domain;

public static class Roles
{
    public const string PlatformAdministrator = "Platform Administrator";
    public const string WorkspaceAdministrator = "Workspace Administrator";
    public const string DataAnalyst = "Data Analyst";
    public const string BusinessUser = "Business User";
    public const string Viewer = "Viewer";

    public static readonly string[] All =
    [
        PlatformAdministrator,
        WorkspaceAdministrator,
        DataAnalyst,
        BusinessUser,
        Viewer
    ];
}
