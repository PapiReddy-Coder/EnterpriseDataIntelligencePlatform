using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseDataIntelligencePlatform.Migrations;

[DbContext(typeof(EnterpriseDataIntelligencePlatform.Data.AppDbContext))]
[Migration("20260803060000_AddDatasetManagementModule")]
public partial class AddDatasetManagementModule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DatasetCategories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DatasetCategories", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Tags",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tags", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Datasets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DataSourceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                DataSourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                DataSourceDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                CurrentVersion = table.Column<int>(type: "int", nullable: false),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Datasets", x => x.Id);
                table.ForeignKey(
                    name: "FK_Datasets_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Datasets_DatasetCategories_CategoryId",
                    column: x => x.CategoryId,
                    principalTable: "DatasetCategories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Datasets_Workspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "Workspaces",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DatasetTags",
            columns: table => new
            {
                DatasetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DatasetTags", x => new { x.DatasetId, x.TagId });
                table.ForeignKey(
                    name: "FK_DatasetTags_Datasets_DatasetId",
                    column: x => x.DatasetId,
                    principalTable: "Datasets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_DatasetTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DatasetVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DatasetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                VersionNumber = table.Column<int>(type: "int", nullable: false),
                IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CategoryName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OwnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                DataSourceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                DataSourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                DataSourceDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                TagsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                VersionNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DatasetVersions", x => x.Id);
                table.ForeignKey(
                    name: "FK_DatasetVersions_Datasets_DatasetId",
                    column: x => x.DatasetId,
                    principalTable: "Datasets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DatasetCategories_NormalizedName",
            table: "DatasetCategories",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Tags_WorkspaceId_NormalizedName",
            table: "Tags",
            columns: new[] { "WorkspaceId", "NormalizedName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Datasets_CategoryId",
            table: "Datasets",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_Datasets_OwnerId",
            table: "Datasets",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_Datasets_WorkspaceId_NormalizedName",
            table: "Datasets",
            columns: new[] { "WorkspaceId", "NormalizedName" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_Datasets_WorkspaceId_Status_UpdatedAtUtc",
            table: "Datasets",
            columns: new[] { "WorkspaceId", "Status", "UpdatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_DatasetTags_TagId",
            table: "DatasetTags",
            column: "TagId");

        migrationBuilder.CreateIndex(
            name: "IX_DatasetVersions_DatasetId_VersionNumber",
            table: "DatasetVersions",
            columns: new[] { "DatasetId", "VersionNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DatasetVersions_WorkspaceId_DatasetId",
            table: "DatasetVersions",
            columns: new[] { "WorkspaceId", "DatasetId" });

        InsertDatasetCategories(migrationBuilder);
        InsertDatasetPermissions(migrationBuilder);

        InsertRolePermissions(
            migrationBuilder,
            "11111111-1111-1111-1111-111111111111",
            Enumerable.Range(19, 9));

        InsertRolePermissions(
            migrationBuilder,
            "22222222-2222-2222-2222-222222222222",
            Enumerable.Range(19, 9));

        InsertRolePermissions(
            migrationBuilder,
            "33333333-3333-3333-3333-333333333333",
            new[] { 19, 20, 21, 22, 23, 25, 26 });

        InsertRolePermissions(
            migrationBuilder,
            "44444444-4444-4444-4444-444444444444",
            new[] { 19, 25 });

        InsertRolePermissions(
            migrationBuilder,
            "55555555-5555-5555-5555-555555555555",
            new[] { 19, 25 });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        DeleteRolePermissions(migrationBuilder);

        for (var number = 19; number <= 27; number++)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyColumnType: "uniqueidentifier",
                keyValue: PermissionId(number));
        }

        migrationBuilder.DropTable(name: "DatasetTags");
        migrationBuilder.DropTable(name: "DatasetVersions");
        migrationBuilder.DropTable(name: "Tags");
        migrationBuilder.DropTable(name: "Datasets");
        migrationBuilder.DropTable(name: "DatasetCategories");
    }

    private static void InsertDatasetCategories(MigrationBuilder migrationBuilder)
    {
        var names = new[]
        {
            "Human Resources",
            "Finance",
            "Sales",
            "Marketing",
            "Operations",
            "Customer",
            "Reference",
            "Other"
        };

        var descriptions = new[]
        {
            "HR and workforce datasets",
            "Financial and accounting datasets",
            "Sales and revenue datasets",
            "Marketing and campaign datasets",
            "Operational datasets",
            "Customer and relationship datasets",
            "Shared master and reference datasets",
            "Unclassified datasets"
        };

        for (var index = 0; index < names.Length; index++)
        {
            migrationBuilder.InsertData(
                table: "DatasetCategories",
                columns: new[]
                {
                    "Id",
                    "Name",
                    "NormalizedName",
                    "Description",
                    "IsActive",
                    "CreatedAtUtc"
                },
                columnTypes: new[]
                {
                    "uniqueidentifier",
                    "nvarchar(100)",
                    "nvarchar(100)",
                    "nvarchar(500)",
                    "bit",
                    "datetime2"
                },
                values: new object[]
                {
                    CategoryId(index + 1),
                    names[index],
                    names[index].ToUpperInvariant(),
                    descriptions[index],
                    true,
                    new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc)
                });
        }
    }

    private static void InsertDatasetPermissions(MigrationBuilder migrationBuilder)
    {
        var permissions = new (int Number, string Name, string Description)[]
        {
            (19, "datasets.view", "View dataset catalog and details"),
            (20, "datasets.create", "Register datasets"),
            (21, "datasets.update", "Update dataset metadata"),
            (22, "datasets.archive", "Archive datasets"),
            (23, "datasets.restore", "Restore archived or soft-deleted datasets"),
            (24, "datasets.delete", "Soft delete datasets"),
            (25, "datasets.versions.view", "View dataset version history"),
            (26, "datasets.versions.restore", "Restore previous dataset metadata versions"),
            (27, "datasets.categories.manage", "Manage dataset category master data")
        };

        foreach (var permission in permissions)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Name", "Description" },
                columnTypes: new[]
                {
                    "uniqueidentifier",
                    "nvarchar(150)",
                    "nvarchar(500)"
                },
                values: new object[]
                {
                    PermissionId(permission.Number),
                    permission.Name,
                    permission.Description
                });
        }
    }

    private static void InsertRolePermissions(
        MigrationBuilder migrationBuilder,
        string roleId,
        IEnumerable<int> permissionNumbers)
    {
        foreach (var number in permissionNumbers)
        {
            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                columnTypes: new[] { "uniqueidentifier", "uniqueidentifier" },
                values: new object[]
                {
                    Guid.Parse(roleId),
                    PermissionId(number)
                });
        }
    }

    private static void DeleteRolePermissions(MigrationBuilder migrationBuilder)
    {
        var rolePermissions = new Dictionary<string, int[]>
        {
            ["11111111-1111-1111-1111-111111111111"] = Enumerable.Range(19, 9).ToArray(),
            ["22222222-2222-2222-2222-222222222222"] = Enumerable.Range(19, 9).ToArray(),
            ["33333333-3333-3333-3333-333333333333"] = new[] { 19, 20, 21, 22, 23, 25, 26 },
            ["44444444-4444-4444-4444-444444444444"] = new[] { 19, 25 },
            ["55555555-5555-5555-5555-555555555555"] = new[] { 19, 25 }
        };

        foreach (var mapping in rolePermissions)
        {
            foreach (var number in mapping.Value)
            {
                migrationBuilder.DeleteData(
                    table: "RolePermissions",
                    keyColumns: new[] { "RoleId", "PermissionId" },
                    keyColumnTypes: new[] { "uniqueidentifier", "uniqueidentifier" },
                    keyValues: new object[]
                    {
                        Guid.Parse(mapping.Key),
                        PermissionId(number)
                    });
            }
        }
    }

    private static Guid CategoryId(int number) =>
        Guid.Parse($"20000000-0000-0000-0000-{number:000000000000}");

    private static Guid PermissionId(int number) =>
        Guid.Parse($"10000000-0000-0000-0000-{number:000000000000}");
}
