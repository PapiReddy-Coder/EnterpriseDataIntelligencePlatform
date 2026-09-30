using EnterpriseDataIntelligencePlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseDataIntelligencePlatform.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260924090000_Task23DataSharingExportSecureDelivery")]
public sealed class Task23DataSharingExportSecureDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
CREATE TABLE dbo.DataExportRequests(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_DataExportRequests PRIMARY KEY,
    WorkspaceId uniqueidentifier NOT NULL,
    DatasetId uniqueidentifier NOT NULL,
    DatasetVersionId uniqueidentifier NULL,
    RequestedByUserId uniqueidentifier NOT NULL,
    Format nvarchar(20) NOT NULL,
    SelectedColumnIdsJson nvarchar(max) NOT NULL,
    FiltersJson nvarchar(max) NOT NULL,
    SortsJson nvarchar(max) NOT NULL,
    Page int NULL,
    PageSize int NULL,
    Status nvarchar(20) NOT NULL,
    RecordCount bigint NULL,
    RequestedAtUtc datetime2 NOT NULL,
    ProcessingStartedAtUtc datetime2 NULL,
    CompletedAtUtc datetime2 NULL,
    ExpiresAtUtc datetime2 NOT NULL,
    StorageKey nvarchar(500) NULL,
    FileName nvarchar(255) NULL,
    FailureReason nvarchar(4000) NULL,
    CONSTRAINT FK_DataExportRequests_Datasets_DatasetId FOREIGN KEY(DatasetId) REFERENCES dbo.Datasets(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_DataExportRequests_DatasetVersions_DatasetVersionId FOREIGN KEY(DatasetVersionId) REFERENCES dbo.DatasetVersions(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_DataExportRequests_AspNetUsers_RequestedByUserId FOREIGN KEY(RequestedByUserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION,
    CONSTRAINT CK_DataExportRequests_Format CHECK (Format IN (N'CSV',N'Excel')),
    CONSTRAINT CK_DataExportRequests_Status CHECK (Status IN (N'Pending',N'Processing',N'Completed',N'Failed',N'Cancelled'))
);
CREATE INDEX IX_DataExportRequests_WorkspaceId_DatasetId_RequestedAtUtc ON dbo.DataExportRequests(WorkspaceId,DatasetId,RequestedAtUtc);
CREATE INDEX IX_DataExportRequests_WorkspaceId_Status_RequestedAtUtc ON dbo.DataExportRequests(WorkspaceId,Status,RequestedAtUtc);
CREATE INDEX IX_DataExportRequests_DatasetId ON dbo.DataExportRequests(DatasetId);
CREATE INDEX IX_DataExportRequests_DatasetVersionId ON dbo.DataExportRequests(DatasetVersionId);
CREATE INDEX IX_DataExportRequests_RequestedByUserId ON dbo.DataExportRequests(RequestedByUserId);

CREATE TABLE dbo.DatasetShareHistories(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_DatasetShareHistories PRIMARY KEY,
    WorkspaceId uniqueidentifier NOT NULL,
    DatasetId uniqueidentifier NOT NULL,
    DatasetAccessGrantId uniqueidentifier NOT NULL,
    RecipientUserId uniqueidentifier NOT NULL,
    SharedByUserId uniqueidentifier NOT NULL,
    AccessLevel nvarchar(20) NOT NULL,
    SharedDateUtc datetime2 NOT NULL,
    ExpiryDateUtc datetime2 NULL,
    Status nvarchar(20) NOT NULL,
    Action nvarchar(20) NOT NULL,
    RevokedDateUtc datetime2 NULL,
    RevokedByUserId uniqueidentifier NULL,
    Reason nvarchar(1000) NULL,
    CONSTRAINT FK_DatasetShareHistories_Datasets_DatasetId FOREIGN KEY(DatasetId) REFERENCES dbo.Datasets(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_DatasetShareHistories_DatasetAccessGrants_DatasetAccessGrantId FOREIGN KEY(DatasetAccessGrantId) REFERENCES dbo.DatasetAccessGrants(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_DatasetShareHistories_AspNetUsers_RecipientUserId FOREIGN KEY(RecipientUserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_DatasetShareHistories_AspNetUsers_SharedByUserId FOREIGN KEY(SharedByUserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_DatasetShareHistories_AspNetUsers_RevokedByUserId FOREIGN KEY(RevokedByUserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION,
    CONSTRAINT CK_DatasetShareHistories_Level CHECK (AccessLevel IN (N'Read',N'Write',N'Manage')),
    CONSTRAINT CK_DatasetShareHistories_Status CHECK (Status IN (N'Active',N'Expired',N'Revoked')),
    CONSTRAINT CK_DatasetShareHistories_Action CHECK (Action IN (N'Shared',N'Updated',N'Revoked'))
);
CREATE INDEX IX_DatasetShareHistories_WorkspaceId_DatasetId_SharedDateUtc ON dbo.DatasetShareHistories(WorkspaceId,DatasetId,SharedDateUtc);
CREATE INDEX IX_DatasetShareHistories_WorkspaceId_RecipientUserId_Status ON dbo.DatasetShareHistories(WorkspaceId,RecipientUserId,Status);
CREATE INDEX IX_DatasetShareHistories_DatasetAccessGrantId ON dbo.DatasetShareHistories(DatasetAccessGrantId);
CREATE INDEX IX_DatasetShareHistories_DatasetId ON dbo.DatasetShareHistories(DatasetId);
CREATE INDEX IX_DatasetShareHistories_RecipientUserId ON dbo.DatasetShareHistories(RecipientUserId);
CREATE INDEX IX_DatasetShareHistories_SharedByUserId ON dbo.DatasetShareHistories(SharedByUserId);
CREATE INDEX IX_DatasetShareHistories_RevokedByUserId ON dbo.DatasetShareHistories(RevokedByUserId);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DROP TABLE dbo.DatasetShareHistories;
DROP TABLE dbo.DataExportRequests;
""");
    }
}
