using EnterpriseDataIntelligencePlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseDataIntelligencePlatform.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260908060000_Task21DataLineageImpactAnalysis")]
public sealed class Task21DataLineageImpactAnalysis : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LineageRelationships",
            schema: "dbo",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                WorkspaceId = table.Column<Guid>(nullable: false),
                DatasetId = table.Column<Guid>(nullable: false),
                OwnerId = table.Column<Guid>(nullable: false),
                SourceEntityType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                SourceEntityId = table.Column<Guid>(nullable: false),
                SourceDatasetId = table.Column<Guid>(nullable: true),
                SourceDisplayName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                SourceVersionNumber = table.Column<int>(nullable: true),
                TargetEntityType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                TargetEntityId = table.Column<Guid>(nullable: false),
                TargetDatasetId = table.Column<Guid>(nullable: true),
                TargetDisplayName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                TargetVersionNumber = table.Column<int>(nullable: true),
                RelationshipType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                RelevantDatasetVersionId = table.Column<Guid>(nullable: true),
                RelevantDatasetVersionNumber = table.Column<int>(nullable: true),
                ProcessEntityType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                ProcessEntityId = table.Column<Guid>(nullable: true),
                MetadataJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                IsAutomatic = table.Column<bool>(nullable: false),
                IsActive = table.Column<bool>(nullable: false),
                CreatedByUserId = table.Column<Guid>(nullable: true),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                UpdatedByUserId = table.Column<Guid>(nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(nullable: true),
                DeactivatedByUserId = table.Column<Guid>(nullable: true),
                DeactivatedAtUtc = table.Column<DateTime>(nullable: true),
                DeactivationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LineageRelationships", x => x.Id);
                table.ForeignKey(
                    name: "FK_LineageRelationships_Datasets_DatasetId",
                    column: x => x.DatasetId,
                    principalSchema: "dbo",
                    principalTable: "Datasets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LineageRelationships_DatasetVersions_RelevantDatasetVersionId",
                    column: x => x.RelevantDatasetVersionId,
                    principalSchema: "dbo",
                    principalTable: "DatasetVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LineageRelationships_Source_Target_Type_Active",
            schema: "dbo", table: "LineageRelationships",
            columns: new[] { "WorkspaceId", "SourceEntityType", "SourceEntityId", "TargetEntityType", "TargetEntityId", "RelationshipType" },
            unique: true, filter: "[IsActive] = 1");
        migrationBuilder.CreateIndex(
            name: "IX_LineageRelationships_Workspace_Dataset_Active_Created",
            schema: "dbo", table: "LineageRelationships",
            columns: new[] { "WorkspaceId", "DatasetId", "IsActive", "CreatedAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_LineageRelationships_Workspace_Source_Active",
            schema: "dbo", table: "LineageRelationships",
            columns: new[] { "WorkspaceId", "SourceEntityType", "SourceEntityId", "IsActive" });
        migrationBuilder.CreateIndex(
            name: "IX_LineageRelationships_Workspace_Target_Active",
            schema: "dbo", table: "LineageRelationships",
            columns: new[] { "WorkspaceId", "TargetEntityType", "TargetEntityId", "IsActive" });
        migrationBuilder.CreateIndex(
            name: "IX_LineageRelationships_RelevantDatasetVersionId",
            schema: "dbo", table: "LineageRelationships", column: "RelevantDatasetVersionId");
        migrationBuilder.CreateIndex(
            name: "IX_LineageRelationships_ProcessEntityId",
            schema: "dbo", table: "LineageRelationships", column: "ProcessEntityId");
        migrationBuilder.CreateIndex(
            name: "IX_LineageRelationships_DatasetId",
            schema: "dbo", table: "LineageRelationships", column: "DatasetId");
        migrationBuilder.CreateIndex(
            name: "IX_LineageRelationships_Workspace_Owner_Created",
            schema: "dbo", table: "LineageRelationships", columns: new[] { "WorkspaceId", "OwnerId", "CreatedAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_LineageRelationships_Workspace_CreatedBy_Created",
            schema: "dbo", table: "LineageRelationships", columns: new[] { "WorkspaceId", "CreatedByUserId", "CreatedAtUtc" });

        migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Name=N'lineage.view')
                INSERT dbo.Permissions(Id,Name,Description) VALUES
                ('10000000-0000-0000-0000-000000000040',N'lineage.view',N'View permitted lineage graphs and relationships');
            IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Name=N'lineage.manage')
                INSERT dbo.Permissions(Id,Name,Description) VALUES
                ('10000000-0000-0000-0000-000000000041',N'lineage.manage',N'Manage lineage relationships within the permitted workspace');
            IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Name=N'lineage.impact')
                INSERT dbo.Permissions(Id,Name,Description) VALUES
                ('10000000-0000-0000-0000-000000000042',N'lineage.impact',N'Run downstream lineage impact analysis');

            DECLARE @Mappings TABLE(RoleName nvarchar(256), PermissionName nvarchar(150));
            INSERT @Mappings VALUES
            (N'Platform Administrator',N'lineage.view'),(N'Platform Administrator',N'lineage.manage'),(N'Platform Administrator',N'lineage.impact'),
            (N'Workspace Administrator',N'lineage.view'),(N'Workspace Administrator',N'lineage.manage'),(N'Workspace Administrator',N'lineage.impact'),
            (N'Data Analyst',N'lineage.view'),(N'Data Analyst',N'lineage.impact'),
            (N'Business User',N'lineage.view'),(N'Viewer',N'lineage.view');
            INSERT dbo.RolePermissions(RoleId,PermissionId)
            SELECT r.Id,p.Id FROM @Mappings m
            JOIN dbo.AspNetRoles r ON r.Name=m.RoleName
            JOIN dbo.Permissions p ON p.Name=m.PermissionName
            WHERE NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LineageRelationships", schema: "dbo");
        migrationBuilder.Sql("""
            DELETE rp FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id=rp.PermissionId
              WHERE p.Name IN (N'lineage.view',N'lineage.manage',N'lineage.impact');
            DELETE FROM dbo.Permissions WHERE Name IN (N'lineage.view',N'lineage.manage',N'lineage.impact');
            """);
    }
}
