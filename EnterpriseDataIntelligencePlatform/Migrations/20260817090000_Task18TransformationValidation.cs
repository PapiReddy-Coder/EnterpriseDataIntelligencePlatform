using EnterpriseDataIntelligencePlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EnterpriseDataIntelligencePlatform.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260817090000_Task18TransformationValidation")]
public sealed class Task18TransformationValidation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "DatasetTransformationConfigurations", schema: "dbo", columns: table => new
        {
            Id = table.Column<Guid>(nullable: false), DatasetId = table.Column<Guid>(nullable: false),
            WorkspaceId = table.Column<Guid>(nullable: false), Version = table.Column<int>(nullable: false),
            IsActive = table.Column<bool>(nullable: false), ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
            CreatedByUserId = table.Column<Guid>(nullable: false), CreatedAtUtc = table.Column<DateTime>(nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_DatasetTransformationConfigurations", x => x.Id);
            table.ForeignKey(
                name: "FK_DatasetTransformationConfigurations_Datasets_DatasetId",
                column: x => x.DatasetId,
                principalSchema: "dbo",
                principalTable: "Datasets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex(name: "IX_DatasetTransformationConfigurations_DatasetId_Version", schema: "dbo", table: "DatasetTransformationConfigurations", columns: new[] { "DatasetId", "Version" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_DatasetTransformationConfigurations_DatasetId_IsActive", schema: "dbo", table: "DatasetTransformationConfigurations", columns: new[] { "DatasetId", "IsActive" }, unique: true, filter: "[IsActive] = 1");
        migrationBuilder.AddColumn<Guid>(name: "TransformationConfigurationId", schema: "dbo", table: "DataImports", nullable: true);
        migrationBuilder.AddColumn<int>(name: "TransformationConfigurationVersion", schema: "dbo", table: "DataImports", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ErrorType", schema: "dbo", table: "ImportErrors", type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Processing Error");
        migrationBuilder.AddColumn<string>(name: "ValidationRule", schema: "dbo", table: "ImportErrors", type: "nvarchar(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>(name: "OriginalValue", schema: "dbo", table: "ImportStagingValues", type: "nvarchar(4000)", maxLength: 4000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TransformedValue", schema: "dbo", table: "ImportStagingValues", type: "nvarchar(4000)", maxLength: 4000, nullable: true);
        migrationBuilder.CreateIndex(name: "IX_DataImports_TransformationConfigurationId", schema: "dbo", table: "DataImports", column: "TransformationConfigurationId");
        migrationBuilder.AddForeignKey(
            name: "FK_DataImports_DatasetTransformationConfigurations_TransformationConfigurationId",
            schema: "dbo",
            table: "DataImports",
            column: "TransformationConfigurationId",
            principalSchema: "dbo",
            principalTable: "DatasetTransformationConfigurations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_DataImports_DatasetTransformationConfigurations_TransformationConfigurationId", schema: "dbo", table: "DataImports");
        migrationBuilder.DropIndex(name: "IX_DataImports_TransformationConfigurationId", schema: "dbo", table: "DataImports");
        migrationBuilder.DropColumn(name: "TransformationConfigurationId", schema: "dbo", table: "DataImports");
        migrationBuilder.DropColumn(name: "TransformationConfigurationVersion", schema: "dbo", table: "DataImports");
        migrationBuilder.DropColumn(name: "ErrorType", schema: "dbo", table: "ImportErrors");
        migrationBuilder.DropColumn(name: "ValidationRule", schema: "dbo", table: "ImportErrors");
        migrationBuilder.DropColumn(name: "OriginalValue", schema: "dbo", table: "ImportStagingValues");
        migrationBuilder.DropColumn(name: "TransformedValue", schema: "dbo", table: "ImportStagingValues");
        migrationBuilder.DropTable(name: "DatasetTransformationConfigurations", schema: "dbo");
    }
}
