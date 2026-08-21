using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseDataIntelligencePlatform.Migrations
{
    public partial class SeedRolesPermissions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Description", "IsGlobal", "Name", "NormalizedName" },
                values: new object[,]
                {
                { new Guid("11111111-1111-1111-1111-111111111111"), "11111111-aaaa-aaaa-aaaa-111111111111", "Predefined Platform Administrator role", true, "Platform Administrator", "PLATFORM ADMINISTRATOR" },
                { new Guid("22222222-2222-2222-2222-222222222222"), "22222222-aaaa-aaaa-aaaa-222222222222", "Predefined Workspace Administrator role", false, "Workspace Administrator", "WORKSPACE ADMINISTRATOR" },
                { new Guid("33333333-3333-3333-3333-333333333333"), "33333333-aaaa-aaaa-aaaa-333333333333", "Predefined Data Analyst role", false, "Data Analyst", "DATA ANALYST" },
                { new Guid("44444444-4444-4444-4444-444444444444"), "44444444-aaaa-aaaa-aaaa-444444444444", "Predefined Business User role", false, "Business User", "BUSINESS USER" },
                { new Guid("55555555-5555-5555-5555-555555555555"), "55555555-aaaa-aaaa-aaaa-555555555555", "Predefined Viewer role", false, "Viewer", "VIEWER" }
                });

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[,]
                {
                { new Guid("10000000-0000-0000-0000-000000000001"), "Manage all workspaces", "workspaces.manage" },
                { new Guid("10000000-0000-0000-0000-000000000002"), "Manage all platform users", "users.manage.all" },
                { new Guid("10000000-0000-0000-0000-000000000003"), "Manage users within a workspace", "users.manage.workspace" },
                { new Guid("10000000-0000-0000-0000-000000000004"), "Assign all platform and workspace roles", "roles.assign.all" },
                { new Guid("10000000-0000-0000-0000-000000000005"), "Assign workspace-specific roles", "roles.assign.workspace" },
                { new Guid("10000000-0000-0000-0000-000000000006"), "View all platform data", "platform.view.all" },
                { new Guid("10000000-0000-0000-0000-000000000007"), "Configure platform settings", "platform.configure" },
                { new Guid("10000000-0000-0000-0000-000000000008"), "Configure workspace settings", "workspace.configure" },
                { new Guid("10000000-0000-0000-0000-000000000009"), "View workspace analytics and reports", "analytics.view" },
                { new Guid("10000000-0000-0000-0000-000000000010"), "Manage datasets", "datasets.manage" },
                { new Guid("10000000-0000-0000-0000-000000000011"), "Configure dashboards", "dashboards.configure" },
                { new Guid("10000000-0000-0000-0000-000000000012"), "Create and modify reports", "reports.modify" },
                { new Guid("10000000-0000-0000-0000-000000000013"), "Execute analytics queries", "analytics.execute" },
                { new Guid("10000000-0000-0000-0000-000000000014"), "View dashboards", "dashboards.view" },
                { new Guid("10000000-0000-0000-0000-000000000015"), "Generate reports", "reports.generate" },
                { new Guid("10000000-0000-0000-0000-000000000016"), "Access business insights", "insights.access" },
                { new Guid("10000000-0000-0000-0000-000000000017"), "Submit data requests", "datarequests.submit" },
                { new Guid("10000000-0000-0000-0000-000000000018"), "Read-only access to reports", "reports.read" }
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                values: new object[,]
                {
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000001") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000002") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000003") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000004") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000005") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000006") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000007") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000009") },
                { new Guid("22222222-2222-2222-2222-222222222222"), new Guid("10000000-0000-0000-0000-000000000003") },
                { new Guid("22222222-2222-2222-2222-222222222222"), new Guid("10000000-0000-0000-0000-000000000005") },
                { new Guid("22222222-2222-2222-2222-222222222222"), new Guid("10000000-0000-0000-0000-000000000008") },
                { new Guid("22222222-2222-2222-2222-222222222222"), new Guid("10000000-0000-0000-0000-000000000009") },
                { new Guid("33333333-3333-3333-3333-333333333333"), new Guid("10000000-0000-0000-0000-000000000010") },
                { new Guid("33333333-3333-3333-3333-333333333333"), new Guid("10000000-0000-0000-0000-000000000011") },
                { new Guid("33333333-3333-3333-3333-333333333333"), new Guid("10000000-0000-0000-0000-000000000012") },
                { new Guid("33333333-3333-3333-3333-333333333333"), new Guid("10000000-0000-0000-0000-000000000013") },
                { new Guid("33333333-3333-3333-3333-333333333333"), new Guid("10000000-0000-0000-0000-000000000009") },
                { new Guid("44444444-4444-4444-4444-444444444444"), new Guid("10000000-0000-0000-0000-000000000014") },
                { new Guid("44444444-4444-4444-4444-444444444444"), new Guid("10000000-0000-0000-0000-000000000015") },
                { new Guid("44444444-4444-4444-4444-444444444444"), new Guid("10000000-0000-0000-0000-000000000016") },
                { new Guid("44444444-4444-4444-4444-444444444444"), new Guid("10000000-0000-0000-0000-000000000017") },
                { new Guid("55555555-5555-5555-5555-555555555555"), new Guid("10000000-0000-0000-0000-000000000014") },
                { new Guid("55555555-5555-5555-5555-555555555555"), new Guid("10000000-0000-0000-0000-000000000018") }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "RoleId", "PermissionId" },
                keyValues: new object[,]
                {
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000001") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000002") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000003") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000004") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000005") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000006") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000007") },
                { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("10000000-0000-0000-0000-000000000009") },
                { new Guid("22222222-2222-2222-2222-222222222222"), new Guid("10000000-0000-0000-0000-000000000003") },
                { new Guid("22222222-2222-2222-2222-222222222222"), new Guid("10000000-0000-0000-0000-000000000005") },
                { new Guid("22222222-2222-2222-2222-222222222222"), new Guid("10000000-0000-0000-0000-000000000008") },
                { new Guid("22222222-2222-2222-2222-222222222222"), new Guid("10000000-0000-0000-0000-000000000009") },
                { new Guid("33333333-3333-3333-3333-333333333333"), new Guid("10000000-0000-0000-0000-000000000010") },
                { new Guid("33333333-3333-3333-3333-333333333333"), new Guid("10000000-0000-0000-0000-000000000011") },
                { new Guid("33333333-3333-3333-3333-333333333333"), new Guid("10000000-0000-0000-0000-000000000012") },
                { new Guid("33333333-3333-3333-3333-333333333333"), new Guid("10000000-0000-0000-0000-000000000013") },
                { new Guid("33333333-3333-3333-3333-333333333333"), new Guid("10000000-0000-0000-0000-000000000009") },
                { new Guid("44444444-4444-4444-4444-444444444444"), new Guid("10000000-0000-0000-0000-000000000014") },
                { new Guid("44444444-4444-4444-4444-444444444444"), new Guid("10000000-0000-0000-0000-000000000015") },
                { new Guid("44444444-4444-4444-4444-444444444444"), new Guid("10000000-0000-0000-0000-000000000016") },
                { new Guid("44444444-4444-4444-4444-444444444444"), new Guid("10000000-0000-0000-0000-000000000017") },
                { new Guid("55555555-5555-5555-5555-555555555555"), new Guid("10000000-0000-0000-0000-000000000014") },
                { new Guid("55555555-5555-5555-5555-555555555555"), new Guid("10000000-0000-0000-0000-000000000018") }
                });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValues: new object[]
                {
new Guid("10000000-0000-0000-0000-000000000001"), new Guid("10000000-0000-0000-0000-000000000002"), new Guid("10000000-0000-0000-0000-000000000003"), new Guid("10000000-0000-0000-0000-000000000004"), new Guid("10000000-0000-0000-0000-000000000005"), new Guid("10000000-0000-0000-0000-000000000006"), new Guid("10000000-0000-0000-0000-000000000007"), new Guid("10000000-0000-0000-0000-000000000008"), new Guid("10000000-0000-0000-0000-000000000009"), new Guid("10000000-0000-0000-0000-000000000010"), new Guid("10000000-0000-0000-0000-000000000011"), new Guid("10000000-0000-0000-0000-000000000012"), new Guid("10000000-0000-0000-0000-000000000013"), new Guid("10000000-0000-0000-0000-000000000014"), new Guid("10000000-0000-0000-0000-000000000015"), new Guid("10000000-0000-0000-0000-000000000016"), new Guid("10000000-0000-0000-0000-000000000017"), new Guid("10000000-0000-0000-0000-000000000018")
                });

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValues: new object[]
                {
new Guid("11111111-1111-1111-1111-111111111111"), new Guid("22222222-2222-2222-2222-222222222222"), new Guid("33333333-3333-3333-3333-333333333333"), new Guid("44444444-4444-4444-4444-444444444444"), new Guid("55555555-5555-5555-5555-555555555555")
                });
        }
    }
}
