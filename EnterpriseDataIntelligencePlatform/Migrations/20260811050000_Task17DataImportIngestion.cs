using EnterpriseDataIntelligencePlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseDataIntelligencePlatform.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260811050000_Task17DataImportIngestion")]
public sealed class Task17DataImportIngestion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE [UploadedDataFiles](
 [Id] uniqueidentifier NOT NULL PRIMARY KEY,[DatasetId] uniqueidentifier NOT NULL,[WorkspaceId] uniqueidentifier NOT NULL,[UploadedByUserId] uniqueidentifier NOT NULL,
 [OriginalFileName] nvarchar(255) NOT NULL,[StoredFileName] nvarchar(255) NOT NULL,[FilePath] nvarchar(1000) NOT NULL,[Extension] nvarchar(10) NOT NULL,[FileSizeBytes] bigint NOT NULL,[UploadedAtUtc] datetime2 NOT NULL,
 CONSTRAINT [FK_UploadedDataFiles_Datasets_DatasetId] FOREIGN KEY([DatasetId]) REFERENCES [Datasets]([Id])
);
CREATE INDEX [IX_UploadedDataFiles_WorkspaceId_DatasetId] ON [UploadedDataFiles]([WorkspaceId],[DatasetId]);

CREATE TABLE [DatasetColumns](
 [Id] uniqueidentifier NOT NULL PRIMARY KEY,[DatasetId] uniqueidentifier NOT NULL,[WorkspaceId] uniqueidentifier NOT NULL,[Name] nvarchar(200) NOT NULL,[NormalizedName] nvarchar(200) NOT NULL,
 [DataType] nvarchar(30) NOT NULL,[Ordinal] int NOT NULL,[IsRequired] bit NOT NULL,[IsKey] bit NOT NULL,[CreatedAtUtc] datetime2 NOT NULL,
 CONSTRAINT [FK_DatasetColumns_Datasets_DatasetId] FOREIGN KEY([DatasetId]) REFERENCES [Datasets]([Id]) ON DELETE CASCADE
);
CREATE UNIQUE INDEX [IX_DatasetColumns_DatasetId_NormalizedName] ON [DatasetColumns]([DatasetId],[NormalizedName]);

CREATE TABLE [DataImports](
 [Id] uniqueidentifier NOT NULL PRIMARY KEY,[DatasetId] uniqueidentifier NOT NULL,[WorkspaceId] uniqueidentifier NOT NULL,[FileId] uniqueidentifier NOT NULL,[InitiatedByUserId] uniqueidentifier NOT NULL,
 [Status] nvarchar(50) NOT NULL,[ImportMode] nvarchar(20) NOT NULL,[DuplicateBehavior] nvarchar(20) NOT NULL,[InvalidRecordBehavior] nvarchar(20) NOT NULL,[CsvDelimiter] nvarchar(5) NOT NULL,
 [FirstRowContainsHeaders] bit NOT NULL,[WorksheetName] nvarchar(255) NULL,[KeyColumnsJson] nvarchar(4000) NOT NULL,[TotalRecords] int NOT NULL,[SuccessfullyImportedRecords] int NOT NULL,
 [RejectedRecords] int NOT NULL,[ErrorCount] int NOT NULL,[CreatedAtUtc] datetime2 NOT NULL,[QueuedAtUtc] datetime2 NULL,[StartedAtUtc] datetime2 NULL,[CompletedAtUtc] datetime2 NULL,
 [FailureMessage] nvarchar(4000) NULL,[CancellationRequested] bit NOT NULL,
 CONSTRAINT [FK_DataImports_Datasets_DatasetId] FOREIGN KEY([DatasetId]) REFERENCES [Datasets]([Id]),
 CONSTRAINT [FK_DataImports_UploadedDataFiles_FileId] FOREIGN KEY([FileId]) REFERENCES [UploadedDataFiles]([Id])
);
CREATE INDEX [IX_DataImports_WorkspaceId_DatasetId_CreatedAtUtc] ON [DataImports]([WorkspaceId],[DatasetId],[CreatedAtUtc]);
CREATE INDEX [IX_DataImports_DatasetId_Status] ON [DataImports]([DatasetId],[Status]);

CREATE TABLE [ImportStagingRows](
 [Id] uniqueidentifier NOT NULL PRIMARY KEY,[ImportId] uniqueidentifier NOT NULL,[WorkspaceId] uniqueidentifier NOT NULL,[RowNumber] int NOT NULL,[KeyHash] nvarchar(64) NOT NULL,
 [IsValid] bit NOT NULL,[IsRejected] bit NOT NULL,[CreatedAtUtc] datetime2 NOT NULL,
 CONSTRAINT [FK_ImportStagingRows_DataImports_ImportId] FOREIGN KEY([ImportId]) REFERENCES [DataImports]([Id]) ON DELETE CASCADE
);
CREATE INDEX [IX_ImportStagingRows_ImportId_RowNumber] ON [ImportStagingRows]([ImportId],[RowNumber]);

CREATE TABLE [ImportStagingValues](
 [Id] uniqueidentifier NOT NULL PRIMARY KEY,[StagingRowId] uniqueidentifier NOT NULL,[ColumnName] nvarchar(200) NOT NULL,[RawValue] nvarchar(4000) NULL,
 CONSTRAINT [FK_ImportStagingValues_ImportStagingRows_StagingRowId] FOREIGN KEY([StagingRowId]) REFERENCES [ImportStagingRows]([Id]) ON DELETE CASCADE
);
CREATE INDEX [IX_ImportStagingValues_StagingRowId] ON [ImportStagingValues]([StagingRowId]);

CREATE TABLE [DatasetRecords](
 [Id] uniqueidentifier NOT NULL PRIMARY KEY,[DatasetId] uniqueidentifier NOT NULL,[WorkspaceId] uniqueidentifier NOT NULL,[SourceImportId] uniqueidentifier NOT NULL,[KeyHash] nvarchar(64) NOT NULL,
 [CreatedAtUtc] datetime2 NOT NULL,[UpdatedAtUtc] datetime2 NOT NULL,
 CONSTRAINT [FK_DatasetRecords_Datasets_DatasetId] FOREIGN KEY([DatasetId]) REFERENCES [Datasets]([Id]) ON DELETE CASCADE
);
CREATE INDEX [IX_DatasetRecords_DatasetId_KeyHash] ON [DatasetRecords]([DatasetId],[KeyHash]);

CREATE TABLE [DatasetRecordValues](
 [Id] uniqueidentifier NOT NULL PRIMARY KEY,[DatasetRecordId] uniqueidentifier NOT NULL,[DatasetColumnId] uniqueidentifier NOT NULL,[RawValue] nvarchar(4000) NULL,[StringValue] nvarchar(4000) NULL,
 [IntegerValue] bigint NULL,[DecimalValue] decimal(38,10) NULL,[BooleanValue] bit NULL,[DateTimeValue] datetime2 NULL,
 CONSTRAINT [FK_DatasetRecordValues_DatasetRecords_DatasetRecordId] FOREIGN KEY([DatasetRecordId]) REFERENCES [DatasetRecords]([Id]) ON DELETE CASCADE,
 CONSTRAINT [FK_DatasetRecordValues_DatasetColumns_DatasetColumnId] FOREIGN KEY([DatasetColumnId]) REFERENCES [DatasetColumns]([Id])
);
CREATE UNIQUE INDEX [IX_DatasetRecordValues_DatasetRecordId_DatasetColumnId] ON [DatasetRecordValues]([DatasetRecordId],[DatasetColumnId]);
CREATE INDEX [IX_DatasetRecordValues_DatasetColumnId] ON [DatasetRecordValues]([DatasetColumnId]);

CREATE TABLE [ImportErrors](
 [Id] uniqueidentifier NOT NULL PRIMARY KEY,[ImportId] uniqueidentifier NOT NULL,[WorkspaceId] uniqueidentifier NOT NULL,[RowNumber] int NULL,[ColumnName] nvarchar(200) NULL,[InvalidValue] nvarchar(1000) NULL,
 [ErrorDescription] nvarchar(2000) NOT NULL,[ErrorTimestampUtc] datetime2 NOT NULL,
 CONSTRAINT [FK_ImportErrors_DataImports_ImportId] FOREIGN KEY([ImportId]) REFERENCES [DataImports]([Id]) ON DELETE CASCADE
);
CREATE INDEX [IX_ImportErrors_ImportId_ErrorTimestampUtc] ON [ImportErrors]([ImportId],[ErrorTimestampUtc]);

INSERT INTO [Permissions]([Id],[Name],[Description]) VALUES
('10000000-0000-0000-0000-000000000028','imports.upload','Upload import files'),
('10000000-0000-0000-0000-000000000029','imports.create','Create dataset imports'),
('10000000-0000-0000-0000-000000000030','imports.start','Start dataset imports'),
('10000000-0000-0000-0000-000000000031','imports.view','View import status and history'),
('10000000-0000-0000-0000-000000000032','imports.errors.view','View import row errors'),
('10000000-0000-0000-0000-000000000033','imports.cancel','Cancel active imports'),
('10000000-0000-0000-0000-000000000034','imports.schema.manage','Manage dataset ingestion key columns');

INSERT INTO [RolePermissions]([RoleId],[PermissionId])
SELECT r.RoleId,p.PermissionId FROM
(VALUES
('11111111-1111-1111-1111-111111111111'),('22222222-2222-2222-2222-222222222222'),('33333333-3333-3333-3333-333333333333')) r(RoleId)
CROSS JOIN
(VALUES
('10000000-0000-0000-0000-000000000028'),('10000000-0000-0000-0000-000000000029'),('10000000-0000-0000-0000-000000000030'),('10000000-0000-0000-0000-000000000031'),('10000000-0000-0000-0000-000000000032'),('10000000-0000-0000-0000-000000000033'),('10000000-0000-0000-0000-000000000034')) p(PermissionId);
INSERT INTO [RolePermissions]([RoleId],[PermissionId]) VALUES
('44444444-4444-4444-4444-444444444444','10000000-0000-0000-0000-000000000031'),
('55555555-5555-5555-5555-555555555555','10000000-0000-0000-0000-000000000031');
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DELETE FROM [RolePermissions] WHERE [PermissionId] IN ('10000000-0000-0000-0000-000000000028','10000000-0000-0000-0000-000000000029','10000000-0000-0000-0000-000000000030','10000000-0000-0000-0000-000000000031','10000000-0000-0000-0000-000000000032','10000000-0000-0000-0000-000000000033','10000000-0000-0000-0000-000000000034');
DELETE FROM [Permissions] WHERE [Id] IN ('10000000-0000-0000-0000-000000000028','10000000-0000-0000-0000-000000000029','10000000-0000-0000-0000-000000000030','10000000-0000-0000-0000-000000000031','10000000-0000-0000-0000-000000000032','10000000-0000-0000-0000-000000000033','10000000-0000-0000-0000-000000000034');
DROP TABLE [ImportErrors]; DROP TABLE [DatasetRecordValues]; DROP TABLE [DatasetRecords]; DROP TABLE [ImportStagingValues]; DROP TABLE [ImportStagingRows]; DROP TABLE [DataImports]; DROP TABLE [DatasetColumns]; DROP TABLE [UploadedDataFiles];
");
    }
}
