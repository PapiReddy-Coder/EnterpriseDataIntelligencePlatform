using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseDataIntelligencePlatform.Migrations
{
    /// <inheritdoc />
    public partial class Task16DatasetVersioningMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateSequence<long>(
                name: "DatasetCodeSequence",
                schema: "dbo",
                startValue: 1L,
                incrementBy: 1);

            // Add Code as nullable first so existing rows can be backfilled safely.
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Datasets",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "DatasetVersions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            // Backfill all existing datasets with globally unique values:
            // DS-000001, DS-000002, ...
            migrationBuilder.Sql(
                """
                DECLARE @DatasetId uniqueidentifier;
                DECLARE @SequenceValue bigint;
                DECLARE @DatasetCode nvarchar(20);

                DECLARE dataset_code_cursor CURSOR LOCAL FAST_FORWARD FOR
                    SELECT Id
                    FROM dbo.Datasets
                    WHERE Code IS NULL OR LTRIM(RTRIM(Code)) = ''
                    ORDER BY CreatedAtUtc, Id;

                OPEN dataset_code_cursor;
                FETCH NEXT FROM dataset_code_cursor INTO @DatasetId;

                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @SequenceValue = NEXT VALUE FOR dbo.DatasetCodeSequence;
                    SET @DatasetCode =
                        'DS-' + RIGHT('000000' + CONVERT(varchar(20), @SequenceValue), 6);

                    UPDATE dbo.Datasets
                    SET Code = @DatasetCode
                    WHERE Id = @DatasetId;

                    FETCH NEXT FROM dataset_code_cursor INTO @DatasetId;
                END

                CLOSE dataset_code_cursor;
                DEALLOCATE dataset_code_cursor;
                """);

            // Every historical metadata snapshot should carry the same immutable
            // Dataset Code as its parent dataset.
            migrationBuilder.Sql(
                """
                UPDATE dv
                SET dv.Code = d.Code
                FROM dbo.DatasetVersions AS dv
                INNER JOIN dbo.Datasets AS d
                    ON d.Id = dv.DatasetId
                WHERE dv.Code IS NULL OR LTRIM(RTRIM(dv.Code)) = '';
                """);

            // If Task 15 data contains the old "Soft Deleted" lifecycle status,
            // keep soft deletion represented by IsDeleted and restore a valid
            // lifecycle status. Prefer the latest valid historical status.
            migrationBuilder.Sql(
                """
                UPDATE d
                SET d.Status = COALESCE(
                    (
                        SELECT TOP (1) dv.Status
                        FROM dbo.DatasetVersions AS dv
                        WHERE dv.DatasetId = d.Id
                          AND dv.Status IN ('Draft', 'Active', 'Archived')
                        ORDER BY dv.VersionNumber DESC
                    ),
                    'Draft'
                )
                FROM dbo.Datasets AS d
                WHERE d.Status = 'Soft Deleted';
                """);

            // Make Dataset Code mandatory after existing data has been backfilled.
            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Datasets",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "DatasetVersions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            // Dataset Code must be globally unique across the platform.
            migrationBuilder.CreateIndex(
                name: "IX_Datasets_Code",
                table: "Datasets",
                column: "Code",
                unique: true);

            // Requires previous versions to be read-only.
            // Remove restore-version grants and then remove the obsolete permission.
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "RoleId", "PermissionId" },
                keyValues: new object[]
                {
                    new Guid("11111111-1111-1111-1111-111111111111"),
                    new Guid("10000000-0000-0000-0000-000000000026")
                });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "RoleId", "PermissionId" },
                keyValues: new object[]
                {
                    new Guid("22222222-2222-2222-2222-222222222222"),
                    new Guid("10000000-0000-0000-0000-000000000026")
                });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "RoleId", "PermissionId" },
                keyValues: new object[]
                {
                    new Guid("33333333-3333-3333-3333-333333333333"),
                    new Guid("10000000-0000-0000-0000-000000000026")
                });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000026"));

            // Data Analyst is the contributor equivalent.
            // Contributors can create/update assigned datasets, but lifecycle
            // management belongs to administrators.
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "RoleId", "PermissionId" },
                keyValues: new object[]
                {
                    new Guid("33333333-3333-3333-3333-333333333333"),
                    new Guid("10000000-0000-0000-0000-000000000022")
                });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "RoleId", "PermissionId" },
                keyValues: new object[]
                {
                    new Guid("33333333-3333-3333-3333-333333333333"),
                    new Guid("10000000-0000-0000-0000-000000000023")
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the Task 15 permission model.
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[]
                {
                    new Guid("10000000-0000-0000-0000-000000000026"),
                    "Restore previous dataset metadata versions",
                    "datasets.versions.restore"
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                values: new object[,]
                {
                    {
                        new Guid("11111111-1111-1111-1111-111111111111"),
                        new Guid("10000000-0000-0000-0000-000000000026")
                    },
                    {
                        new Guid("22222222-2222-2222-2222-222222222222"),
                        new Guid("10000000-0000-0000-0000-000000000026")
                    },
                    {
                        new Guid("33333333-3333-3333-3333-333333333333"),
                        new Guid("10000000-0000-0000-0000-000000000026")
                    },
                    {
                        new Guid("33333333-3333-3333-3333-333333333333"),
                        new Guid("10000000-0000-0000-0000-000000000022")
                    },
                    {
                        new Guid("33333333-3333-3333-3333-333333333333"),
                        new Guid("10000000-0000-0000-0000-000000000023")
                    }
                });

            migrationBuilder.DropIndex(
                name: "IX_Datasets_Code",
                table: "Datasets");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "DatasetVersions");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Datasets");

            migrationBuilder.DropSequence(
                name: "DatasetCodeSequence",
                schema: "dbo");
        }
    }
}
