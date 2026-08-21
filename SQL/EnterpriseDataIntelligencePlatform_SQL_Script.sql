/*
==============================================================================
Enterprise Data Intelligence Platform
Final Database Seed and Verification Script - Updated Through Task 18
==============================================================================

Latest module:
- Task 18 Assignment - Data Transformation, Mapping & Validation Rules Module

Purpose:
- Verifies authentication, workspace, user, role, permission, refresh-token,
  audit-log, leave-management, dataset catalog, versioning, metadata and
  data-ingestion tables.
- Seeds and validates predefined roles, permissions and role-permission mappings.
- Verifies globally unique Dataset Codes, categories, tags, metadata versions,
  search/filter behavior, soft delete, RBAC and workspace isolation.
- Verifies secured CSV/XLSX upload metadata, inferred schemas, import lifecycle,
  Full/Append modes, duplicate handling, staging data, typed records, row-level
  errors, import history, cancellation and audit coverage.
- Applies and verifies Task 18 transformation configuration versioning, import
  configuration pinning, original/transformed staging values and validation metadata.

Important:
- The database schema is managed using Entity Framework Core migrations.
- Apply all migrations before executing this script:

      Update-Database

  or:

      dotnet ef database update

- If the base schema through Task 17 exists but Task 18 is missing, this script
  applies/repairs the Task 18 objects and records the matching migration history.

- Application users must be created through ASP.NET Core Identity UserManager.
- The script is designed for repeatable local setup and verification.

Expected tables:
- Workspaces
- AspNetUsers
- AspNetRoles
- AspNetUserRoles
- AspNetUserClaims
- AspNetRoleClaims
- AspNetUserLogins
- AspNetUserTokens
- Permissions
- RolePermissions
- RefreshTokens
- AuditLogs
- LeaveRequests
- DatasetCategories
- Datasets
- Tags
- DatasetTags
- DatasetVersions
- DatasetTransformationConfigurations
- UploadedDataFiles
- DatasetColumns
- DataImports
- ImportStagingRows
- ImportStagingValues
- DatasetRecords
- DatasetRecordValues
- ImportErrors
- __EFMigrationsHistory

Task 18 schema changes included in this script:
- New table: DatasetTransformationConfigurations
- Extended table: DataImports
  - TransformationConfigurationId
  - TransformationConfigurationVersion
- Extended table: ImportErrors
  - ErrorType
  - ValidationRule
- Extended table: ImportStagingValues
  - OriginalValue
  - TransformedValue

==============================================================================
*/

------------------------------------------------------------
-- Create Database If Missing
------------------------------------------------------------
IF DB_ID(N'EnterpriseDataIntelligencePlatform') IS NULL
BEGIN
    CREATE DATABASE [EnterpriseDataIntelligencePlatform];
END
GO

USE [EnterpriseDataIntelligencePlatform];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

------------------------------------------------------------
-- Verify Required Base Tables Exist (through Task 17)
------------------------------------------------------------
DECLARE @MissingTables NVARCHAR(MAX);

SELECT @MissingTables = STRING_AGG(RequiredTable.TableName, N', ')
FROM
(
    VALUES
        
        (N'Workspaces'),
        (N'AspNetUsers'),
        (N'AspNetRoles'),
        (N'AspNetUserRoles'),
        (N'AspNetUserClaims'),
        (N'AspNetRoleClaims'),
        (N'AspNetUserLogins'),
        (N'AspNetUserTokens'),
        (N'Permissions'),
        (N'RolePermissions'),
        (N'RefreshTokens'),
        (N'AuditLogs'),
        (N'LeaveRequests'),
        (N'DatasetCategories'),
        (N'Datasets'),
        (N'Tags'),
        (N'DatasetTags'),
        (N'DatasetVersions'),
        (N'UploadedDataFiles'),
        (N'DatasetColumns'),
        (N'DataImports'),
        (N'ImportStagingRows'),
        (N'ImportStagingValues'),
        (N'DatasetRecords'),
        (N'DatasetRecordValues'),
        (N'ImportErrors'),
        (N'__EFMigrationsHistory')
) AS RequiredTable(TableName)
WHERE OBJECT_ID(N'dbo.' + RequiredTable.TableName, N'U') IS NULL;

IF @MissingTables IS NOT NULL
BEGIN
    DECLARE @MissingMessage NVARCHAR(2048) =
        N'Required tables are missing: ' + @MissingTables
        + N'. Run Entity Framework Core migrations first.';

    THROW 50001, @MissingMessage, 1;
END
GO

------------------------------------------------------------
-- Apply / Repair Task 18 Transformation Schema
-- This batch is intentionally placed before seed and verification batches so
-- earlier verification failures cannot hide the Task 18 schema changes.
------------------------------------------------------------
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.DatasetTransformationConfigurations', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DatasetTransformationConfigurations
        (
            Id uniqueidentifier NOT NULL
                CONSTRAINT PK_DatasetTransformationConfigurations PRIMARY KEY,
            DatasetId uniqueidentifier NOT NULL,
            WorkspaceId uniqueidentifier NOT NULL,
            Version int NOT NULL,
            IsActive bit NOT NULL,
            ConfigurationJson nvarchar(max) NOT NULL,
            CreatedByUserId uniqueidentifier NOT NULL,
            CreatedAtUtc datetime2 NOT NULL,
            CONSTRAINT FK_DatasetTransformationConfigurations_Datasets_DatasetId
                FOREIGN KEY (DatasetId) REFERENCES dbo.Datasets(Id)
                ON DELETE NO ACTION
        );
    END;

    IF OBJECT_ID
       (
           N'dbo.FK_DatasetTransformationConfigurations_Datasets_DatasetId',
           N'F'
       ) IS NULL
    BEGIN
        IF OBJECT_ID(N'dbo.FK_DatasetTransformationConfigurations_Datasets', N'F') IS NOT NULL
            EXEC sys.sp_rename
                N'dbo.FK_DatasetTransformationConfigurations_Datasets',
                N'FK_DatasetTransformationConfigurations_Datasets_DatasetId',
                N'OBJECT';
        ELSE
            ALTER TABLE dbo.DatasetTransformationConfigurations WITH CHECK
                ADD CONSTRAINT FK_DatasetTransformationConfigurations_Datasets_DatasetId
                FOREIGN KEY (DatasetId)
                REFERENCES dbo.Datasets(Id)
                ON DELETE NO ACTION;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.DatasetTransformationConfigurations', N'U')
          AND name = N'IX_DatasetTransformationConfigurations_DatasetId_Version'
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.DatasetTransformationConfigurations', N'U')
              AND name = N'UX_DatasetTransformationConfigurations_Dataset_Version'
        )
            EXEC sys.sp_rename
                N'dbo.DatasetTransformationConfigurations.UX_DatasetTransformationConfigurations_Dataset_Version',
                N'IX_DatasetTransformationConfigurations_DatasetId_Version',
                N'INDEX';
        ELSE
            CREATE UNIQUE INDEX IX_DatasetTransformationConfigurations_DatasetId_Version
                ON dbo.DatasetTransformationConfigurations(DatasetId, Version);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.DatasetTransformationConfigurations', N'U')
          AND name = N'IX_DatasetTransformationConfigurations_DatasetId_IsActive'
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.DatasetTransformationConfigurations', N'U')
              AND name = N'UX_DatasetTransformationConfigurations_Active'
        )
            EXEC sys.sp_rename
                N'dbo.DatasetTransformationConfigurations.UX_DatasetTransformationConfigurations_Active',
                N'IX_DatasetTransformationConfigurations_DatasetId_IsActive',
                N'INDEX';
        ELSE
            CREATE UNIQUE INDEX IX_DatasetTransformationConfigurations_DatasetId_IsActive
                ON dbo.DatasetTransformationConfigurations(DatasetId, IsActive)
                WHERE IsActive = 1;
    END;

    IF COL_LENGTH(N'dbo.DataImports', N'TransformationConfigurationId') IS NULL
        ALTER TABLE dbo.DataImports
            ADD TransformationConfigurationId uniqueidentifier NULL;

    IF COL_LENGTH(N'dbo.DataImports', N'TransformationConfigurationVersion') IS NULL
        ALTER TABLE dbo.DataImports
            ADD TransformationConfigurationVersion int NULL;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.DataImports', N'U')
          AND name = N'IX_DataImports_TransformationConfigurationId'
    )
        CREATE INDEX IX_DataImports_TransformationConfigurationId
            ON dbo.DataImports(TransformationConfigurationId);

    IF OBJECT_ID
       (
           N'dbo.FK_DataImports_DatasetTransformationConfigurations_TransformationConfigurationId',
           N'F'
       ) IS NULL
    BEGIN
        IF OBJECT_ID(N'dbo.FK_DataImports_TransformationConfiguration', N'F') IS NOT NULL
            EXEC sys.sp_rename
                N'dbo.FK_DataImports_TransformationConfiguration',
                N'FK_DataImports_DatasetTransformationConfigurations_TransformationConfigurationId',
                N'OBJECT';
        ELSE
            ALTER TABLE dbo.DataImports WITH CHECK
                ADD CONSTRAINT FK_DataImports_DatasetTransformationConfigurations_TransformationConfigurationId
                FOREIGN KEY (TransformationConfigurationId)
                REFERENCES dbo.DatasetTransformationConfigurations(Id)
                ON DELETE NO ACTION;
    END;

    IF COL_LENGTH(N'dbo.ImportErrors', N'ErrorType') IS NULL
        ALTER TABLE dbo.ImportErrors
            ADD ErrorType nvarchar(50) NOT NULL
                CONSTRAINT DF_ImportErrors_ErrorType DEFAULT N'Processing Error';

    IF COL_LENGTH(N'dbo.ImportErrors', N'ValidationRule') IS NULL
        ALTER TABLE dbo.ImportErrors
            ADD ValidationRule nvarchar(100) NULL;

    IF COL_LENGTH(N'dbo.ImportStagingValues', N'OriginalValue') IS NULL
        ALTER TABLE dbo.ImportStagingValues
            ADD OriginalValue nvarchar(4000) NULL;

    IF COL_LENGTH(N'dbo.ImportStagingValues', N'TransformedValue') IS NULL
        ALTER TABLE dbo.ImportStagingValues
            ADD TransformedValue nvarchar(4000) NULL;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.__EFMigrationsHistory
        WHERE MigrationId = N'20260817090000_Task18TransformationValidation'
    )
        INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
        VALUES (N'20260817090000_Task18TransformationValidation', N'8.0.22');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

PRINT N'Task 18 schema applied successfully to database [' + DB_NAME()
    + N']. Refresh the SSMS Tables node to display dbo.DatasetTransformationConfigurations.';

SELECT
    DB_NAME() AS DatabaseName,
    SCHEMA_NAME(TableRecord.schema_id) AS SchemaName,
    TableRecord.name AS TableName,
    N'Available' AS Task18TableStatus,
    CASE
        WHEN EXISTS
             (
                 SELECT 1
                 FROM dbo.__EFMigrationsHistory
                 WHERE MigrationId = N'20260817090000_Task18TransformationValidation'
             )
        THEN N'Applied'
        ELSE N'Missing'
    END AS Task18MigrationStatus
FROM sys.tables AS TableRecord
WHERE TableRecord.object_id = OBJECT_ID(N'dbo.DatasetTransformationConfigurations', N'U');
GO

------------------------------------------------------------
-- Seed Predefined Roles
------------------------------------------------------------
BEGIN TRY
    BEGIN TRANSACTION;

    MERGE dbo.AspNetRoles AS Target
    USING
    (
        VALUES
        (
            CAST('11111111-1111-1111-1111-111111111111' AS UNIQUEIDENTIFIER),
            N'Platform Administrator',
            N'PLATFORM ADMINISTRATOR',
            N'11111111-aaaa-aaaa-aaaa-111111111111',
            CAST(1 AS BIT),
            N'Predefined Platform Administrator role'
        ),
        (
            CAST('22222222-2222-2222-2222-222222222222' AS UNIQUEIDENTIFIER),
            N'Workspace Administrator',
            N'WORKSPACE ADMINISTRATOR',
            N'22222222-aaaa-aaaa-aaaa-222222222222',
            CAST(0 AS BIT),
            N'Predefined Workspace Administrator role'
        ),
        (
            CAST('33333333-3333-3333-3333-333333333333' AS UNIQUEIDENTIFIER),
            N'Data Analyst',
            N'DATA ANALYST',
            N'33333333-aaaa-aaaa-aaaa-333333333333',
            CAST(0 AS BIT),
            N'Predefined Data Analyst role'
        ),
        (
            CAST('44444444-4444-4444-4444-444444444444' AS UNIQUEIDENTIFIER),
            N'Business User',
            N'BUSINESS USER',
            N'44444444-aaaa-aaaa-aaaa-444444444444',
            CAST(0 AS BIT),
            N'Predefined Business User role'
        ),
        (
            CAST('55555555-5555-5555-5555-555555555555' AS UNIQUEIDENTIFIER),
            N'Viewer',
            N'VIEWER',
            N'55555555-aaaa-aaaa-aaaa-555555555555',
            CAST(0 AS BIT),
            N'Predefined Viewer role'
        )
    ) AS Source
    (
        Id,
        Name,
        NormalizedName,
        ConcurrencyStamp,
        IsGlobal,
        Description
    )
        ON Target.Id = Source.Id

    WHEN MATCHED THEN
        UPDATE SET
            Target.Name = Source.Name,
            Target.NormalizedName = Source.NormalizedName,
            Target.IsGlobal = Source.IsGlobal,
            Target.Description = Source.Description

    WHEN NOT MATCHED BY TARGET THEN
        INSERT
        (
            Id,
            Name,
            NormalizedName,
            ConcurrencyStamp,
            IsGlobal,
            Description
        )
        VALUES
        (
            Source.Id,
            Source.Name,
            Source.NormalizedName,
            Source.ConcurrencyStamp,
            Source.IsGlobal,
            Source.Description
        );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO

------------------------------------------------------------
-- Seed Permissions
------------------------------------------------------------
BEGIN TRY
    BEGIN TRANSACTION;

    MERGE dbo.Permissions AS Target
    USING
    (
        VALUES
        (CAST('10000000-0000-0000-0000-000000000001' AS UNIQUEIDENTIFIER), N'workspaces.manage',             N'Manage all workspaces'),
        (CAST('10000000-0000-0000-0000-000000000002' AS UNIQUEIDENTIFIER), N'users.manage.all',              N'Manage all platform users'),
        (CAST('10000000-0000-0000-0000-000000000003' AS UNIQUEIDENTIFIER), N'users.manage.workspace',        N'Manage users within a workspace'),
        (CAST('10000000-0000-0000-0000-000000000004' AS UNIQUEIDENTIFIER), N'roles.assign.all',              N'Assign all platform and workspace roles'),
        (CAST('10000000-0000-0000-0000-000000000005' AS UNIQUEIDENTIFIER), N'roles.assign.workspace',        N'Assign workspace-specific roles'),
        (CAST('10000000-0000-0000-0000-000000000006' AS UNIQUEIDENTIFIER), N'platform.view.all',             N'View all platform data'),
        (CAST('10000000-0000-0000-0000-000000000007' AS UNIQUEIDENTIFIER), N'platform.configure',            N'Configure platform settings'),
        (CAST('10000000-0000-0000-0000-000000000008' AS UNIQUEIDENTIFIER), N'workspace.configure',           N'Configure workspace settings'),
        (CAST('10000000-0000-0000-0000-000000000009' AS UNIQUEIDENTIFIER), N'analytics.view',                N'View workspace analytics and reports'),
        (CAST('10000000-0000-0000-0000-000000000010' AS UNIQUEIDENTIFIER), N'datasets.manage',               N'Legacy dataset management permission retained for compatibility'),
        (CAST('10000000-0000-0000-0000-000000000011' AS UNIQUEIDENTIFIER), N'dashboards.configure',          N'Configure dashboards'),
        (CAST('10000000-0000-0000-0000-000000000012' AS UNIQUEIDENTIFIER), N'reports.modify',                N'Create and modify reports'),
        (CAST('10000000-0000-0000-0000-000000000013' AS UNIQUEIDENTIFIER), N'analytics.execute',             N'Execute analytics queries'),
        (CAST('10000000-0000-0000-0000-000000000014' AS UNIQUEIDENTIFIER), N'dashboards.view',               N'View dashboards'),
        (CAST('10000000-0000-0000-0000-000000000015' AS UNIQUEIDENTIFIER), N'reports.generate',              N'Generate reports'),
        (CAST('10000000-0000-0000-0000-000000000016' AS UNIQUEIDENTIFIER), N'insights.access',               N'Access business insights'),
        (CAST('10000000-0000-0000-0000-000000000017' AS UNIQUEIDENTIFIER), N'datarequests.submit',           N'Submit data requests'),
        (CAST('10000000-0000-0000-0000-000000000018' AS UNIQUEIDENTIFIER), N'reports.read',                  N'Read-only access to reports'),

        -- Dataset catalog, versioning and metadata permissions
        (CAST('10000000-0000-0000-0000-000000000019' AS UNIQUEIDENTIFIER), N'datasets.view',                 N'View dataset catalog, details and permitted metadata'),
        (CAST('10000000-0000-0000-0000-000000000020' AS UNIQUEIDENTIFIER), N'datasets.create',               N'Create/register datasets'),
        (CAST('10000000-0000-0000-0000-000000000021' AS UNIQUEIDENTIFIER), N'datasets.update',               N'Update dataset metadata'),
        (CAST('10000000-0000-0000-0000-000000000022' AS UNIQUEIDENTIFIER), N'datasets.archive',              N'Archive datasets'),
        (CAST('10000000-0000-0000-0000-000000000023' AS UNIQUEIDENTIFIER), N'datasets.restore',              N'Restore archived or administratively recover soft-deleted datasets'),
        (CAST('10000000-0000-0000-0000-000000000024' AS UNIQUEIDENTIFIER), N'datasets.delete',               N'Soft delete datasets'),
        (CAST('10000000-0000-0000-0000-000000000025' AS UNIQUEIDENTIFIER), N'datasets.versions.view',        N'View read-only dataset version history and previous versions'),
        (CAST('10000000-0000-0000-0000-000000000027' AS UNIQUEIDENTIFIER), N'datasets.categories.manage',    N'Manage dataset category master data'),

        -- File import and ingestion permissions
        (CAST('10000000-0000-0000-0000-000000000028' AS UNIQUEIDENTIFIER), N'imports.upload',                N'Upload CSV and XLSX import files'),
        (CAST('10000000-0000-0000-0000-000000000029' AS UNIQUEIDENTIFIER), N'imports.create',                N'Configure dataset imports'),
        (CAST('10000000-0000-0000-0000-000000000030' AS UNIQUEIDENTIFIER), N'imports.start',                 N'Queue dataset imports for background processing'),
        (CAST('10000000-0000-0000-0000-000000000031' AS UNIQUEIDENTIFIER), N'imports.view',                  N'View import status and history'),
        (CAST('10000000-0000-0000-0000-000000000032' AS UNIQUEIDENTIFIER), N'imports.errors.view',           N'View persistent row-level import errors'),
        (CAST('10000000-0000-0000-0000-000000000033' AS UNIQUEIDENTIFIER), N'imports.cancel',                N'Cancel queued or processing imports'),
        (CAST('10000000-0000-0000-0000-000000000034' AS UNIQUEIDENTIFIER), N'imports.schema.manage',         N'Manage dataset ingestion key columns')
    ) AS Source(Id, Name, Description)
        ON Target.Id = Source.Id

    WHEN MATCHED THEN
        UPDATE SET
            Target.Name = Source.Name,
            Target.Description = Source.Description

    WHEN NOT MATCHED BY TARGET THEN
        INSERT
        (
            Id,
            Name,
            Description
        )
        VALUES
        (
            Source.Id,
            Source.Name,
            Source.Description
        );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO

------------------------------------------------------------
-- Enforce Immutable Historical Dataset Versions
------------------------------------------------------------
BEGIN TRY
    BEGIN TRANSACTION;

    DELETE Mapping
    FROM dbo.RolePermissions AS Mapping
    INNER JOIN dbo.Permissions AS PermissionRecord
        ON PermissionRecord.Id = Mapping.PermissionId
    WHERE PermissionRecord.Id = '10000000-0000-0000-0000-000000000026'
       OR PermissionRecord.Name = N'datasets.versions.restore';

    DELETE FROM dbo.Permissions
    WHERE Id = '10000000-0000-0000-0000-000000000026'
       OR Name = N'datasets.versions.restore';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO

------------------------------------------------------------
-- Seed Role-Permission Mappings
------------------------------------------------------------
BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @RequiredRolePermissions TABLE
    (
        RoleId UNIQUEIDENTIFIER NOT NULL,
        PermissionId UNIQUEIDENTIFIER NOT NULL,
        PRIMARY KEY (RoleId, PermissionId)
    );

    INSERT INTO @RequiredRolePermissions (RoleId, PermissionId)
    VALUES
        -- Platform Administrator - existing platform permissions
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000001'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000002'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000003'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000004'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000005'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000006'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000007'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000009'),

        -- Workspace Administrator - existing workspace permissions
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000003'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000005'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000008'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000009'),

        -- Data Analyst - existing analytics permissions
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000009'),
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000010'),
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000011'),
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000012'),
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000013'),

        -- Business User - existing read/report permissions
        ('44444444-4444-4444-4444-444444444444', '10000000-0000-0000-0000-000000000014'),
        ('44444444-4444-4444-4444-444444444444', '10000000-0000-0000-0000-000000000015'),
        ('44444444-4444-4444-4444-444444444444', '10000000-0000-0000-0000-000000000016'),
        ('44444444-4444-4444-4444-444444444444', '10000000-0000-0000-0000-000000000017'),

        -- Viewer - existing read-only permissions
        ('55555555-5555-5555-5555-555555555555', '10000000-0000-0000-0000-000000000014'),
        ('55555555-5555-5555-5555-555555555555', '10000000-0000-0000-0000-000000000018'),

        -- Platform Administrator - full dataset access
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000019'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000020'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000021'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000022'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000023'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000024'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000025'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000027'),

        -- Workspace Administrator - manage datasets in own workspace
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000019'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000020'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000021'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000022'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000023'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000024'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000025'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000027'),

        -- Data Analyst is the contributor equivalent
        -- Service layer must additionally enforce OwnerId = current Data Analyst.
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000019'),
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000020'),
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000021'),
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000025'),

        -- Business User and Viewer are read-only
        ('44444444-4444-4444-4444-444444444444', '10000000-0000-0000-0000-000000000019'),
        ('44444444-4444-4444-4444-444444444444', '10000000-0000-0000-0000-000000000025'),
        ('55555555-5555-5555-5555-555555555555', '10000000-0000-0000-0000-000000000019'),
        ('55555555-5555-5555-5555-555555555555', '10000000-0000-0000-0000-000000000025'),

        -- Platform Administrator - full import and ingestion access
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000028'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000029'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000030'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000031'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000032'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000033'),
        ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000034'),

        -- Workspace Administrator - full access within own workspace
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000028'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000029'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000030'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000031'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000032'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000033'),
        ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000034'),

        -- Data Analyst - ingestion access for owned datasets
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000028'),
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000029'),
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000030'),
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000031'),
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000032'),
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000033'),
        ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000034'),

        -- Business User and Viewer - read-only import history
        ('44444444-4444-4444-4444-444444444444', '10000000-0000-0000-0000-000000000031'),
        ('55555555-5555-5555-5555-555555555555', '10000000-0000-0000-0000-000000000031');

    -- Remove obsolete or over-privileged feature mappings for the predefined roles.
    DELETE Existing
    FROM dbo.RolePermissions AS Existing
    INNER JOIN dbo.Permissions AS PermissionRecord
        ON PermissionRecord.Id = Existing.PermissionId
    WHERE Existing.RoleId IN
    (
        '11111111-1111-1111-1111-111111111111',
        '22222222-2222-2222-2222-222222222222',
        '33333333-3333-3333-3333-333333333333',
        '44444444-4444-4444-4444-444444444444',
        '55555555-5555-5555-5555-555555555555'
    )
      AND (PermissionRecord.Name LIKE N'datasets.%'
           OR PermissionRecord.Name LIKE N'imports.%')
      AND NOT EXISTS
      (
          SELECT 1
          FROM @RequiredRolePermissions AS RequiredMapping
          WHERE RequiredMapping.RoleId = Existing.RoleId
            AND RequiredMapping.PermissionId = Existing.PermissionId
      );

    INSERT INTO dbo.RolePermissions
    (
        RoleId,
        PermissionId
    )
    SELECT
        Source.RoleId,
        Source.PermissionId
    FROM @RequiredRolePermissions AS Source
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.RolePermissions AS Existing
        WHERE Existing.RoleId = Source.RoleId
          AND Existing.PermissionId = Source.PermissionId
    );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO

------------------------------------------------------------
-- Supporting Indexes
------------------------------------------------------------
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_AspNetUsers_WorkspaceId'
      AND object_id = OBJECT_ID(N'dbo.AspNetUsers')
)
BEGIN
    EXEC sys.sp_executesql
        N'CREATE INDEX IX_AspNetUsers_WorkspaceId ON dbo.AspNetUsers (WorkspaceId);';
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_AuditLogs_UserId'
      AND object_id = OBJECT_ID(N'dbo.AuditLogs')
)
BEGIN
    EXEC sys.sp_executesql
        N'CREATE INDEX IX_AuditLogs_UserId ON dbo.AuditLogs (UserId);';
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_AuditLogs_WorkspaceId'
      AND object_id = OBJECT_ID(N'dbo.AuditLogs')
)
BEGIN
    EXEC sys.sp_executesql
        N'CREATE INDEX IX_AuditLogs_WorkspaceId ON dbo.AuditLogs (WorkspaceId);';
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_AuditLogs_CreatedAtUtc'
      AND object_id = OBJECT_ID(N'dbo.AuditLogs')
)
BEGIN
    EXEC sys.sp_executesql
        N'CREATE INDEX IX_AuditLogs_CreatedAtUtc ON dbo.AuditLogs (CreatedAtUtc);';
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LeaveRequests_WorkspaceId'
      AND object_id = OBJECT_ID(N'dbo.LeaveRequests')
)
BEGIN
    EXEC sys.sp_executesql
        N'CREATE INDEX IX_LeaveRequests_WorkspaceId ON dbo.LeaveRequests (WorkspaceId);';
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LeaveRequests_UserId'
      AND object_id = OBJECT_ID(N'dbo.LeaveRequests')
)
BEGIN
    EXEC sys.sp_executesql
        N'CREATE INDEX IX_LeaveRequests_UserId ON dbo.LeaveRequests (UserId);';
END
GO

------------------------------------------------------------
-- Leave Request Foreign-Key Hardening
------------------------------------------------------------
IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_LeaveRequests_AspNetUsers_UserId'
)
BEGIN
    ALTER TABLE dbo.LeaveRequests WITH CHECK
    ADD CONSTRAINT FK_LeaveRequests_AspNetUsers_UserId
        FOREIGN KEY (UserId)
        REFERENCES dbo.AspNetUsers (Id);

    ALTER TABLE dbo.LeaveRequests
        CHECK CONSTRAINT FK_LeaveRequests_AspNetUsers_UserId;
END
GO

------------------------------------------------------------
-- Current Database / Server Verification
------------------------------------------------------------
SELECT
    DB_NAME() AS ActiveDatabase,
    @@SERVERNAME AS SqlServerInstance,
    SYSUTCDATETIME() AS VerifiedAtUtc;
GO

------------------------------------------------------------
-- Complete Table Verification
------------------------------------------------------------
SELECT
    RequiredTable.TableName,
    CASE
        WHEN ExistingTable.object_id IS NULL THEN N'Missing'
        ELSE N'Available'
    END AS TableStatus
FROM
(
    VALUES
        
        (N'Workspaces'),
        (N'AspNetUsers'),
        (N'AspNetRoles'),
        (N'AspNetUserRoles'),
        (N'AspNetUserClaims'),
        (N'AspNetRoleClaims'),
        (N'AspNetUserLogins'),
        (N'AspNetUserTokens'),
        (N'Permissions'),
        (N'RolePermissions'),
        (N'RefreshTokens'),
        (N'AuditLogs'),
        (N'LeaveRequests'),
        (N'DatasetCategories'),
        (N'Datasets'),
        (N'Tags'),
        (N'DatasetTags'),
        (N'DatasetVersions'),
        (N'DatasetTransformationConfigurations'),
        (N'UploadedDataFiles'),
        (N'DatasetColumns'),
        (N'DataImports'),
        (N'ImportStagingRows'),
        (N'ImportStagingValues'),
        (N'DatasetRecords'),
        (N'DatasetRecordValues'),
        (N'ImportErrors'),
        (N'__EFMigrationsHistory')
) AS RequiredTable(TableName)
LEFT JOIN sys.tables AS ExistingTable
    ON ExistingTable.name = RequiredTable.TableName
ORDER BY RequiredTable.TableName;
GO

------------------------------------------------------------
-- Role Verification
------------------------------------------------------------
SELECT
    Id,
    Name,
    NormalizedName,
    IsGlobal,
    Description
FROM dbo.AspNetRoles
ORDER BY
    CASE Name
        WHEN N'Platform Administrator' THEN 1
        WHEN N'Workspace Administrator' THEN 2
        WHEN N'Data Analyst' THEN 3
        WHEN N'Business User' THEN 4
        WHEN N'Viewer' THEN 5
        ELSE 99
    END;
GO

------------------------------------------------------------
-- Permission Verification
------------------------------------------------------------
SELECT
    Id,
    Name,
    Description
FROM dbo.Permissions
ORDER BY Name;
GO

------------------------------------------------------------
-- Role-Permission Verification
------------------------------------------------------------
SELECT
    RoleRecord.Name AS RoleName,
    PermissionRecord.Name AS PermissionName,
    PermissionRecord.Description
FROM dbo.RolePermissions AS Mapping
INNER JOIN dbo.AspNetRoles AS RoleRecord
    ON RoleRecord.Id = Mapping.RoleId
INNER JOIN dbo.Permissions AS PermissionRecord
    ON PermissionRecord.Id = Mapping.PermissionId
ORDER BY RoleRecord.Name, PermissionRecord.Name;
GO

------------------------------------------------------------
-- Role-Permission Count Verification
------------------------------------------------------------
SELECT
    RoleRecord.Name AS RoleName,
    COUNT(Mapping.PermissionId) AS PermissionCount
FROM dbo.AspNetRoles AS RoleRecord
LEFT JOIN dbo.RolePermissions AS Mapping
    ON Mapping.RoleId = RoleRecord.Id
WHERE RoleRecord.Id IN
(
    '11111111-1111-1111-1111-111111111111',
    '22222222-2222-2222-2222-222222222222',
    '33333333-3333-3333-3333-333333333333',
    '44444444-4444-4444-4444-444444444444',
    '55555555-5555-5555-5555-555555555555'
)
GROUP BY RoleRecord.Name
ORDER BY RoleRecord.Name;
GO

------------------------------------------------------------
-- Workspace and User Verification
------------------------------------------------------------
SELECT
    WorkspaceRecord.Id,
    WorkspaceRecord.Name,
    WorkspaceRecord.Code,
    WorkspaceRecord.IsActive,
    WorkspaceRecord.CreatedAtUtc,
    WorkspaceRecord.UpdatedAtUtc,
    COUNT(UserRecord.Id) AS UserCount
FROM dbo.Workspaces AS WorkspaceRecord
LEFT JOIN dbo.AspNetUsers AS UserRecord
    ON UserRecord.WorkspaceId = WorkspaceRecord.Id
GROUP BY
    WorkspaceRecord.Id,
    WorkspaceRecord.Name,
    WorkspaceRecord.Code,
    WorkspaceRecord.IsActive,
    WorkspaceRecord.CreatedAtUtc,
    WorkspaceRecord.UpdatedAtUtc
ORDER BY WorkspaceRecord.Name;
GO

SELECT
    UserRecord.Id,
    UserRecord.FullName,
    UserRecord.Email,
    UserRecord.IsActive,
    UserRecord.WorkspaceId,
    WorkspaceRecord.Name AS WorkspaceName,
    RoleRecord.Name AS RoleName,
    UserRecord.CreatedAtUtc
FROM dbo.AspNetUsers AS UserRecord
LEFT JOIN dbo.Workspaces AS WorkspaceRecord
    ON WorkspaceRecord.Id = UserRecord.WorkspaceId
LEFT JOIN dbo.AspNetUserRoles AS UserRole
    ON UserRole.UserId = UserRecord.Id
LEFT JOIN dbo.AspNetRoles AS RoleRecord
    ON RoleRecord.Id = UserRole.RoleId
ORDER BY UserRecord.CreatedAtUtc DESC;
GO

------------------------------------------------------------
-- Refresh Token / Session Verification
------------------------------------------------------------
SELECT TOP (50)
    TokenRecord.Id,
    TokenRecord.UserId,
    UserRecord.Email,
    TokenRecord.SessionId,
    TokenRecord.ExpiresAtUtc,
    TokenRecord.CreatedAtUtc,
    TokenRecord.RevokedAtUtc,
    CASE
        WHEN TokenRecord.RevokedAtUtc IS NULL
         AND TokenRecord.ExpiresAtUtc > SYSUTCDATETIME()
            THEN N'Active'
        ELSE N'Inactive'
    END AS TokenStatus
FROM dbo.RefreshTokens AS TokenRecord
INNER JOIN dbo.AspNetUsers AS UserRecord
    ON UserRecord.Id = TokenRecord.UserId
ORDER BY TokenRecord.CreatedAtUtc DESC;
GO

------------------------------------------------------------
-- Audit Log Verification
------------------------------------------------------------
SELECT TOP (100)
    Id,
    UserId,
    WorkspaceId,
    Action,
    EntityType,
    EntityId,
    Details,
    IpAddress,
    CreatedAtUtc
FROM dbo.AuditLogs
ORDER BY CreatedAtUtc DESC;
GO

------------------------------------------------------------
-- Workspace Isolation Verification
------------------------------------------------------------
SELECT
    WorkspaceId,
    COUNT(*) AS LeaveRequestCount
FROM dbo.LeaveRequests
GROUP BY WorkspaceId
ORDER BY WorkspaceId;
GO

------------------------------------------------------------
-- EF Core Migration History Verification
------------------------------------------------------------
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
BEGIN
    SELECT
        MigrationId,
        ProductVersion
    FROM dbo.__EFMigrationsHistory
    ORDER BY MigrationId;
END
ELSE
BEGIN
    PRINT N'dbo.__EFMigrationsHistory was not found. Run Update-Database first.';
END
GO

------------------------------------------------------------
-- Final Database Verification Summary
------------------------------------------------------------
SELECT
    (SELECT COUNT(*) FROM dbo.AspNetRoles
     WHERE Id IN
     (
        '11111111-1111-1111-1111-111111111111',
        '22222222-2222-2222-2222-222222222222',
        '33333333-3333-3333-3333-333333333333',
        '44444444-4444-4444-4444-444444444444',
        '55555555-5555-5555-5555-555555555555'
     )) AS SeededRoleCount,

    (SELECT COUNT(*) FROM dbo.Permissions
     WHERE Id IN
     (
        '10000000-0000-0000-0000-000000000001',
        '10000000-0000-0000-0000-000000000002',
        '10000000-0000-0000-0000-000000000003',
        '10000000-0000-0000-0000-000000000004',
        '10000000-0000-0000-0000-000000000005',
        '10000000-0000-0000-0000-000000000006',
        '10000000-0000-0000-0000-000000000007',
        '10000000-0000-0000-0000-000000000008',
        '10000000-0000-0000-0000-000000000009',
        '10000000-0000-0000-0000-000000000010',
        '10000000-0000-0000-0000-000000000011',
        '10000000-0000-0000-0000-000000000012',
        '10000000-0000-0000-0000-000000000013',
        '10000000-0000-0000-0000-000000000014',
        '10000000-0000-0000-0000-000000000015',
        '10000000-0000-0000-0000-000000000016',
        '10000000-0000-0000-0000-000000000017',
        '10000000-0000-0000-0000-000000000018',
        '10000000-0000-0000-0000-000000000019',
        '10000000-0000-0000-0000-000000000020',
        '10000000-0000-0000-0000-000000000021',
        '10000000-0000-0000-0000-000000000022',
        '10000000-0000-0000-0000-000000000023',
        '10000000-0000-0000-0000-000000000024',
        '10000000-0000-0000-0000-000000000025',
        '10000000-0000-0000-0000-000000000027',
        '10000000-0000-0000-0000-000000000028',
        '10000000-0000-0000-0000-000000000029',
        '10000000-0000-0000-0000-000000000030',
        '10000000-0000-0000-0000-000000000031',
        '10000000-0000-0000-0000-000000000032',
        '10000000-0000-0000-0000-000000000033',
        '10000000-0000-0000-0000-000000000034'
     )) AS SeededPermissionCount,

    (SELECT COUNT(*) FROM dbo.RolePermissions
     WHERE RoleId IN
     (
        '11111111-1111-1111-1111-111111111111',
        '22222222-2222-2222-2222-222222222222',
        '33333333-3333-3333-3333-333333333333',
        '44444444-4444-4444-4444-444444444444',
        '55555555-5555-5555-5555-555555555555'
     )) AS RolePermissionMappingCount,

    (SELECT COUNT(*) FROM dbo.Workspaces) AS WorkspaceCount,
    (SELECT COUNT(*) FROM dbo.AspNetUsers) AS UserCount,
    (SELECT COUNT(*) FROM dbo.RefreshTokens) AS RefreshTokenCount,
    (SELECT COUNT(*) FROM dbo.AuditLogs) AS AuditLogCount,
    (SELECT COUNT(*) FROM dbo.Datasets) AS DatasetCount,
    (SELECT COUNT(*) FROM dbo.DatasetVersions) AS DatasetVersionCount,
    (SELECT COUNT(*) FROM dbo.UploadedDataFiles) AS UploadedFileCount,
    (SELECT COUNT(*) FROM dbo.DataImports) AS DataImportCount,
    (SELECT COUNT(*) FROM dbo.ImportErrors) AS ImportErrorCount;
GO

------------------------------------------------------------
-- Expected Seed Counts
------------------------------------------------------------
/*
Expected predefined seed results:

Roles                     : 5
Permissions               : 33 (18 platform + 8 dataset + 7 import permissions)
Role-Permission mappings  : 70 (23 platform + 24 dataset + 23 import mappings)

No default user is inserted by this script.
Create the Platform Administrator through the application's secure
administrative bootstrap process or ASP.NET Core Identity UserManager.
*/
GO

------------------------------------------------------------
-- Development Reset Commands - Use Carefully
------------------------------------------------------------
/*
WARNING:
Run only in a development database.

DELETE FROM dbo.AspNetUserRoles;
DELETE FROM dbo.RolePermissions;
DELETE FROM dbo.Permissions;

DELETE FROM dbo.AspNetRoles
WHERE Id IN
(
    '11111111-1111-1111-1111-111111111111',
    '22222222-2222-2222-2222-222222222222',
    '33333333-3333-3333-3333-333333333333',
    '44444444-4444-4444-4444-444444444444',
    '55555555-5555-5555-5555-555555555555'
);
*/
GO


------------------------------------------------------------
-- Category Master Verification
------------------------------------------------------------
SELECT Id, Name, Description, IsActive, CreatedAtUtc, UpdatedAtUtc
FROM dbo.DatasetCategories
ORDER BY Name;
GO

------------------------------------------------------------
-- Dataset Catalog / Metadata Verification
------------------------------------------------------------
SELECT
    D.Id,
    D.Code,
    D.Name,
    D.Description,
    D.WorkspaceId,
    W.Name AS WorkspaceName,
    D.CategoryId,
    C.Name AS CategoryName,
    D.OwnerId,
    U.FullName AS OwnerName,
    D.DataSourceName,
    D.DataSourceType,
    D.DataSourceDescription,
    D.Status,
    D.CurrentVersion,
    D.IsDeleted,
    D.DeletedAtUtc,
    D.DeletedByUserId,
    D.CreatedAtUtc,
    D.UpdatedAtUtc
FROM dbo.Datasets AS D
INNER JOIN dbo.Workspaces AS W
    ON W.Id = D.WorkspaceId
INNER JOIN dbo.DatasetCategories AS C
    ON C.Id = D.CategoryId
INNER JOIN dbo.AspNetUsers AS U
    ON U.Id = D.OwnerId
ORDER BY D.UpdatedAtUtc DESC, D.Name;
GO

------------------------------------------------------------
-- Tags / Dataset-Tag Mapping Verification
------------------------------------------------------------
SELECT
    D.Code AS DatasetCode,
    D.Name AS DatasetName,
    D.WorkspaceId AS DatasetWorkspaceId,
    T.Id AS TagId,
    T.Name AS TagName,
    T.NormalizedName,
    T.WorkspaceId AS TagWorkspaceId
FROM dbo.DatasetTags AS DT
INNER JOIN dbo.Datasets AS D
    ON D.Id = DT.DatasetId
INNER JOIN dbo.Tags AS T
    ON T.Id = DT.TagId
ORDER BY D.Code, T.Name;
GO

------------------------------------------------------------
-- Duplicate Tag Validation
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    WorkspaceId,
    NormalizedName,
    COUNT(*) AS DuplicateCount
FROM dbo.Tags
GROUP BY WorkspaceId, NormalizedName
HAVING COUNT(*) > 1;
GO

------------------------------------------------------------
-- Duplicate Dataset-Tag Mapping Validation
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    DatasetId,
    TagId,
    COUNT(*) AS DuplicateCount
FROM dbo.DatasetTags
GROUP BY DatasetId, TagId
HAVING COUNT(*) > 1;
GO

------------------------------------------------------------
-- Dataset Version History Verification
------------------------------------------------------------
SELECT
    DV.DatasetId,
    DV.Code,
    DV.VersionNumber,
    DV.IsCurrent,
    DV.Name,
    DV.Description,
    DV.CategoryId,
    DV.CategoryName,
    DV.OwnerId,
    DV.OwnerName,
    DV.DataSourceName,
    DV.DataSourceType,
    DV.DataSourceDescription,
    DV.Status,
    DV.TagsJson,
    DV.VersionNotes,
    DV.CreatedByUserId,
    DV.CreatedAtUtc
FROM dbo.DatasetVersions AS DV
ORDER BY DV.DatasetId, DV.VersionNumber DESC;
GO

------------------------------------------------------------
-- Current Version Consistency
-- Every dataset should have exactly one current snapshot matching CurrentVersion.
-- Expected ValidationStatus = OK
------------------------------------------------------------
SELECT
    D.Id AS DatasetId,
    D.Code,
    D.Name,
    D.CurrentVersion,
    SUM(CASE WHEN DV.IsCurrent = 1 THEN 1 ELSE 0 END) AS CurrentSnapshotCount,
    MAX(CASE WHEN DV.IsCurrent = 1 THEN DV.VersionNumber END) AS CurrentSnapshotVersion,
    CASE
        WHEN COUNT(DV.Id) = 0 THEN N'MISSING VERSION HISTORY'
        WHEN SUM(CASE WHEN DV.IsCurrent = 1 THEN 1 ELSE 0 END) <> 1 THEN N'INVALID CURRENT SNAPSHOT COUNT'
        WHEN MAX(CASE WHEN DV.IsCurrent = 1 THEN DV.VersionNumber END) <> D.CurrentVersion THEN N'VERSION MISMATCH'
        ELSE N'OK'
    END AS ValidationStatus
FROM dbo.Datasets AS D
LEFT JOIN dbo.DatasetVersions AS DV
    ON DV.DatasetId = D.Id
GROUP BY D.Id, D.Code, D.Name, D.CurrentVersion
ORDER BY D.Code;
GO

------------------------------------------------------------
-- Version 1 Presence
-- Every dataset must have an initial Version 1.
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    D.Id,
    D.Code,
    D.Name,
    D.CurrentVersion
FROM dbo.Datasets AS D
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.DatasetVersions AS DV
    WHERE DV.DatasetId = D.Id
      AND DV.VersionNumber = 1
);
GO

------------------------------------------------------------
-- Dataset Permission Verification
------------------------------------------------------------
SELECT R.Name AS RoleName,
       P.Name AS PermissionName
FROM dbo.RolePermissions RP
INNER JOIN dbo.AspNetRoles R ON R.Id = RP.RoleId
INNER JOIN dbo.Permissions P ON P.Id = RP.PermissionId
WHERE P.Name LIKE 'datasets.%'
ORDER BY R.Name, P.Name;
GO

------------------------------------------------------------
-- Dataset Workspace Isolation Summary
------------------------------------------------------------
SELECT WorkspaceId,
       COUNT(*) AS DatasetCount,
       SUM(CASE WHEN Status = 'Active' AND IsDeleted = 0 THEN 1 ELSE 0 END) AS ActiveCount,
       SUM(CASE WHEN Status = 'Archived' AND IsDeleted = 0 THEN 1 ELSE 0 END) AS ArchivedCount,
       SUM(CASE WHEN IsDeleted = 1 THEN 1 ELSE 0 END) AS SoftDeletedCount
FROM dbo.Datasets
GROUP BY WorkspaceId
ORDER BY WorkspaceId;
GO


-- ============================================================================
-- DATASET CATALOG AND VERSION VERIFICATION
-- ============================================================================

------------------------------------------------------------
-- Dataset Code Sequence Verification
------------------------------------------------------------
SELECT
    SCHEMA_NAME(schema_id) AS SequenceSchema,
    name AS SequenceName,
    start_value,
    increment,
    current_value
FROM sys.sequences
WHERE name = N'DatasetCodeSequence';
GO

------------------------------------------------------------
-- Dataset Code Column / Global Unique Index Verification
------------------------------------------------------------
SELECT
    C.name AS ColumnName,
    TYPE_NAME(C.user_type_id) AS DataType,
    C.max_length AS MaxLengthBytes,
    C.is_nullable AS IsNullable
FROM sys.columns AS C
WHERE C.object_id = OBJECT_ID(N'dbo.Datasets')
  AND C.name = N'Code';
GO

SELECT
    I.name AS IndexName,
    I.is_unique AS IsUnique,
    COL_NAME(IC.object_id, IC.column_id) AS ColumnName
FROM sys.indexes AS I
INNER JOIN sys.index_columns AS IC
    ON IC.object_id = I.object_id
   AND IC.index_id = I.index_id
WHERE I.object_id = OBJECT_ID(N'dbo.Datasets')
  AND I.name = N'IX_Datasets_Code';
GO

------------------------------------------------------------
-- Missing Dataset Code Validation
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    Id,
    Code,
    Name
FROM dbo.Datasets
WHERE Code IS NULL
   OR LTRIM(RTRIM(Code)) = N'';
GO

------------------------------------------------------------
-- Duplicate Dataset Code Validation
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    Code,
    COUNT(*) AS DuplicateCount
FROM dbo.Datasets
GROUP BY Code
HAVING COUNT(*) > 1;
GO

------------------------------------------------------------
-- Dataset Code Format Validation
-- Expected: 0 rows for DS-000001 style codes
------------------------------------------------------------
SELECT
    Id,
    Code,
    Name
FROM dbo.Datasets
WHERE Code NOT LIKE N'DS-[0-9][0-9][0-9][0-9][0-9][0-9]';
GO

------------------------------------------------------------
-- Lifecycle Status Validation
-- Status must only be Draft, Active or Archived.
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    Id,
    Code,
    Name,
    Status,
    IsDeleted
FROM dbo.Datasets
WHERE Status NOT IN (N'Draft', N'Active', N'Archived');
GO

------------------------------------------------------------
-- Soft Delete Verification
-- Soft delete is represented by IsDeleted, not by lifecycle Status.
------------------------------------------------------------
SELECT
    Id,
    Code,
    Name,
    Status,
    IsDeleted,
    DeletedAtUtc,
    DeletedByUserId
FROM dbo.Datasets
WHERE IsDeleted = 1
ORDER BY DeletedAtUtc DESC;
GO

------------------------------------------------------------
-- Soft Delete Consistency
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    Id,
    Code,
    Name,
    Status,
    IsDeleted,
    DeletedAtUtc
FROM dbo.Datasets
WHERE (IsDeleted = 1 AND DeletedAtUtc IS NULL)
   OR (IsDeleted = 0 AND DeletedAtUtc IS NOT NULL);
GO

------------------------------------------------------------
-- Mandatory Relationship Validation
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    D.Id,
    D.Code,
    D.Name,
    CASE WHEN W.Id IS NULL THEN N'Missing Workspace' END AS WorkspaceIssue,
    CASE WHEN C.Id IS NULL THEN N'Missing Category' END AS CategoryIssue,
    CASE WHEN U.Id IS NULL THEN N'Missing Owner' END AS OwnerIssue
FROM dbo.Datasets AS D
LEFT JOIN dbo.Workspaces AS W
    ON W.Id = D.WorkspaceId
LEFT JOIN dbo.DatasetCategories AS C
    ON C.Id = D.CategoryId
LEFT JOIN dbo.AspNetUsers AS U
    ON U.Id = D.OwnerId
WHERE W.Id IS NULL
   OR C.Id IS NULL
   OR U.Id IS NULL;
GO

------------------------------------------------------------
-- Owner / Workspace Consistency
-- Dataset Owner must be active and belong to the same workspace.
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    D.Id AS DatasetId,
    D.Code,
    D.Name AS DatasetName,
    D.WorkspaceId AS DatasetWorkspaceId,
    U.Id AS OwnerId,
    U.FullName AS OwnerName,
    U.WorkspaceId AS OwnerWorkspaceId,
    U.IsActive AS OwnerIsActive
FROM dbo.Datasets AS D
INNER JOIN dbo.AspNetUsers AS U
    ON U.Id = D.OwnerId
WHERE U.IsActive = 0
   OR U.WorkspaceId IS NULL
   OR U.WorkspaceId <> D.WorkspaceId;
GO

------------------------------------------------------------
-- Tag / Workspace Consistency
-- Dataset and its tag should belong to the same workspace.
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    D.Id AS DatasetId,
    D.Code,
    D.WorkspaceId AS DatasetWorkspaceId,
    T.Id AS TagId,
    T.Name AS TagName,
    T.WorkspaceId AS TagWorkspaceId
FROM dbo.DatasetTags AS DT
INNER JOIN dbo.Datasets AS D
    ON D.Id = DT.DatasetId
INNER JOIN dbo.Tags AS T
    ON T.Id = DT.TagId
WHERE D.WorkspaceId <> T.WorkspaceId;
GO

------------------------------------------------------------
-- Search / Filter Verification - Dataset Name
-- Change @DatasetNameSearch as needed.
------------------------------------------------------------
DECLARE @DatasetNameSearch NVARCHAR(200) = N'finance';

SELECT
    Id,
    Code,
    Name,
    Status,
    WorkspaceId,
    CategoryId,
    OwnerId,
    CreatedAtUtc,
    UpdatedAtUtc
FROM dbo.Datasets
WHERE IsDeleted = 0
  AND Name LIKE N'%' + @DatasetNameSearch + N'%'
ORDER BY UpdatedAtUtc DESC;
GO

------------------------------------------------------------
-- Search / Filter Verification - Category / Workspace / Owner / Status
-- Replace NULL values with actual IDs/status to test specific filters.
------------------------------------------------------------
DECLARE @CategoryId UNIQUEIDENTIFIER = NULL;
DECLARE @WorkspaceId UNIQUEIDENTIFIER = NULL;
DECLARE @OwnerId UNIQUEIDENTIFIER = NULL;
DECLARE @Status NVARCHAR(30) = NULL;

SELECT
    D.Id,
    D.Code,
    D.Name,
    D.WorkspaceId,
    D.CategoryId,
    D.OwnerId,
    D.Status,
    D.CreatedAtUtc,
    D.UpdatedAtUtc
FROM dbo.Datasets AS D
WHERE D.IsDeleted = 0
  AND (@CategoryId IS NULL OR D.CategoryId = @CategoryId)
  AND (@WorkspaceId IS NULL OR D.WorkspaceId = @WorkspaceId)
  AND (@OwnerId IS NULL OR D.OwnerId = @OwnerId)
  AND (@Status IS NULL OR D.Status = @Status)
ORDER BY D.UpdatedAtUtc DESC;
GO

------------------------------------------------------------
-- Search / Filter Verification - Tag
------------------------------------------------------------
DECLARE @TagSearch NVARCHAR(100) = N'finance';

SELECT DISTINCT
    D.Id,
    D.Code,
    D.Name,
    T.Name AS MatchedTag,
    D.Status,
    D.UpdatedAtUtc
FROM dbo.Datasets AS D
INNER JOIN dbo.DatasetTags AS DT
    ON DT.DatasetId = D.Id
INNER JOIN dbo.Tags AS T
    ON T.Id = DT.TagId
WHERE D.IsDeleted = 0
  AND T.NormalizedName = UPPER(LTRIM(RTRIM(@TagSearch)))
ORDER BY D.UpdatedAtUtc DESC;
GO

------------------------------------------------------------
-- Search / Filter Verification - Created Date Range
-- Adjust dates as required.
------------------------------------------------------------
DECLARE @CreatedFromUtc DATETIME2 = '2026-08-01T00:00:00';
DECLARE @CreatedToUtc   DATETIME2 = '2026-08-31T23:59:59.9999999';

SELECT
    Id,
    Code,
    Name,
    Status,
    CreatedAtUtc,
    UpdatedAtUtc
FROM dbo.Datasets
WHERE IsDeleted = 0
  AND CreatedAtUtc >= @CreatedFromUtc
  AND CreatedAtUtc <= @CreatedToUtc
ORDER BY UpdatedAtUtc DESC;
GO

------------------------------------------------------------
-- Default Sort / Pagination Reference
-- Default sort: Last Updated Date DESC
-- Default page size: 20
-- Maximum page size: 100
------------------------------------------------------------
DECLARE @Page INT = 1;
DECLARE @PageSize INT = 20;

IF @Page < 1 SET @Page = 1;
IF @PageSize < 1 SET @PageSize = 20;
IF @PageSize > 100 SET @PageSize = 100;

SELECT
    Id,
    Code,
    Name,
    Status,
    CurrentVersion,
    CreatedAtUtc,
    UpdatedAtUtc
FROM dbo.Datasets
WHERE IsDeleted = 0
ORDER BY UpdatedAtUtc DESC
OFFSET (@Page - 1) * @PageSize ROWS
FETCH NEXT @PageSize ROWS ONLY;
GO

------------------------------------------------------------
-- Dataset Permission Matrix
------------------------------------------------------------
SELECT
    R.Name AS RoleName,
    P.Name AS PermissionName,
    P.Description
FROM dbo.RolePermissions AS RP
INNER JOIN dbo.AspNetRoles AS R
    ON R.Id = RP.RoleId
INNER JOIN dbo.Permissions AS P
    ON P.Id = RP.PermissionId
WHERE P.Name IN
(
    N'datasets.view',
    N'datasets.create',
    N'datasets.update',
    N'datasets.archive',
    N'datasets.restore',
    N'datasets.delete',
    N'datasets.versions.view',
    N'datasets.categories.manage'
)
ORDER BY
    CASE R.Name
        WHEN N'Platform Administrator' THEN 1
        WHEN N'Workspace Administrator' THEN 2
        WHEN N'Data Analyst' THEN 3
        WHEN N'Business User' THEN 4
        WHEN N'Viewer' THEN 5
        ELSE 99
    END,
    P.Name;
GO

------------------------------------------------------------
-- Read-Only Versioning Requirement
-- There must be NO datasets.versions.restore permission.
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    Id,
    Name,
    Description
FROM dbo.Permissions
WHERE Name = N'datasets.versions.restore';
GO

------------------------------------------------------------
-- Dataset Audit Verification
------------------------------------------------------------
SELECT TOP (100)
    Id,
    UserId,
    WorkspaceId,
    Action,
    EntityType,
    EntityId,
    Details,
    IpAddress,
    CreatedAtUtc
FROM dbo.AuditLogs
WHERE EntityType = N'Dataset'
   OR Action LIKE N'Dataset%'
ORDER BY CreatedAtUtc DESC;
GO

------------------------------------------------------------
-- Database Design / Normalized Tables Verification
------------------------------------------------------------
SELECT
    T.name AS TableName
FROM sys.tables AS T
WHERE T.name IN
(
    N'Datasets',
    N'DatasetVersions',
    N'DatasetCategories',
    N'Tags',
    N'DatasetTags'
)
ORDER BY T.name;
GO

------------------------------------------------------------
-- Final Dataset Verification Summary
------------------------------------------------------------
SELECT
    (SELECT COUNT(*) FROM dbo.DatasetCategories WHERE IsActive = 1) AS ActiveCategoryCount,
    (SELECT COUNT(*) FROM dbo.Datasets) AS TotalDatasetCount,
    (SELECT COUNT(*) FROM dbo.Datasets WHERE IsDeleted = 0) AS AvailableDatasetCount,
    (SELECT COUNT(*) FROM dbo.Datasets WHERE Status = N'Draft' AND IsDeleted = 0) AS DraftDatasetCount,
    (SELECT COUNT(*) FROM dbo.Datasets WHERE Status = N'Active' AND IsDeleted = 0) AS ActiveDatasetCount,
    (SELECT COUNT(*) FROM dbo.Datasets WHERE Status = N'Archived' AND IsDeleted = 0) AS ArchivedDatasetCount,
    (SELECT COUNT(*) FROM dbo.Datasets WHERE IsDeleted = 1) AS SoftDeletedDatasetCount,
    (SELECT COUNT(*) FROM dbo.DatasetVersions) AS DatasetVersionCount,
    (SELECT COUNT(*) FROM dbo.Tags) AS TagCount,
    (SELECT COUNT(*) FROM dbo.DatasetTags) AS DatasetTagMappingCount,
    (SELECT COUNT(*) FROM dbo.Permissions WHERE Name LIKE N'datasets.%') AS DatasetPermissionCount;
GO

-- ============================================================================
-- FILE IMPORT PROCESSING VERIFICATION
-- ============================================================================

------------------------------------------------------------
-- Ingestion Table Contract Verification
-- Expected: 0 rows
------------------------------------------------------------
DECLARE @RequiredIngestionColumns TABLE
(
    TableName SYSNAME NOT NULL,
    ColumnName SYSNAME NOT NULL,
    PRIMARY KEY (TableName, ColumnName)
);

INSERT INTO @RequiredIngestionColumns (TableName, ColumnName)
VALUES
    (N'UploadedDataFiles', N'Id'),
    (N'UploadedDataFiles', N'DatasetId'),
    (N'UploadedDataFiles', N'WorkspaceId'),
    (N'UploadedDataFiles', N'UploadedByUserId'),
    (N'UploadedDataFiles', N'OriginalFileName'),
    (N'UploadedDataFiles', N'StoredFileName'),
    (N'UploadedDataFiles', N'FilePath'),
    (N'UploadedDataFiles', N'Extension'),
    (N'UploadedDataFiles', N'FileSizeBytes'),
    (N'UploadedDataFiles', N'UploadedAtUtc'),
    (N'DatasetColumns', N'Id'),
    (N'DatasetColumns', N'DatasetId'),
    (N'DatasetColumns', N'WorkspaceId'),
    (N'DatasetColumns', N'Name'),
    (N'DatasetColumns', N'NormalizedName'),
    (N'DatasetColumns', N'DataType'),
    (N'DatasetColumns', N'Ordinal'),
    (N'DatasetColumns', N'IsRequired'),
    (N'DatasetColumns', N'IsKey'),
    (N'DatasetColumns', N'CreatedAtUtc'),
    (N'DataImports', N'Id'),
    (N'DataImports', N'DatasetId'),
    (N'DataImports', N'WorkspaceId'),
    (N'DataImports', N'FileId'),
    (N'DataImports', N'InitiatedByUserId'),
    (N'DataImports', N'Status'),
    (N'DataImports', N'ImportMode'),
    (N'DataImports', N'DuplicateBehavior'),
    (N'DataImports', N'InvalidRecordBehavior'),
    (N'DataImports', N'CsvDelimiter'),
    (N'DataImports', N'FirstRowContainsHeaders'),
    (N'DataImports', N'WorksheetName'),
    (N'DataImports', N'KeyColumnsJson'),
    (N'DataImports', N'TotalRecords'),
    (N'DataImports', N'SuccessfullyImportedRecords'),
    (N'DataImports', N'RejectedRecords'),
    (N'DataImports', N'ErrorCount'),
    (N'DataImports', N'CreatedAtUtc'),
    (N'DataImports', N'QueuedAtUtc'),
    (N'DataImports', N'StartedAtUtc'),
    (N'DataImports', N'CompletedAtUtc'),
    (N'DataImports', N'FailureMessage'),
    (N'DataImports', N'CancellationRequested'),
    (N'DataImports', N'TransformationConfigurationId'),
    (N'DataImports', N'TransformationConfigurationVersion'),
    (N'ImportStagingRows', N'Id'),
    (N'ImportStagingRows', N'ImportId'),
    (N'ImportStagingRows', N'WorkspaceId'),
    (N'ImportStagingRows', N'RowNumber'),
    (N'ImportStagingRows', N'KeyHash'),
    (N'ImportStagingRows', N'IsValid'),
    (N'ImportStagingRows', N'IsRejected'),
    (N'ImportStagingRows', N'CreatedAtUtc'),
    (N'ImportStagingValues', N'Id'),
    (N'ImportStagingValues', N'StagingRowId'),
    (N'ImportStagingValues', N'ColumnName'),
    (N'ImportStagingValues', N'RawValue'),
    (N'ImportStagingValues', N'OriginalValue'),
    (N'ImportStagingValues', N'TransformedValue'),
    (N'DatasetRecords', N'Id'),
    (N'DatasetRecords', N'DatasetId'),
    (N'DatasetRecords', N'WorkspaceId'),
    (N'DatasetRecords', N'SourceImportId'),
    (N'DatasetRecords', N'KeyHash'),
    (N'DatasetRecords', N'CreatedAtUtc'),
    (N'DatasetRecords', N'UpdatedAtUtc'),
    (N'DatasetRecordValues', N'Id'),
    (N'DatasetRecordValues', N'DatasetRecordId'),
    (N'DatasetRecordValues', N'DatasetColumnId'),
    (N'DatasetRecordValues', N'RawValue'),
    (N'DatasetRecordValues', N'StringValue'),
    (N'DatasetRecordValues', N'IntegerValue'),
    (N'DatasetRecordValues', N'DecimalValue'),
    (N'DatasetRecordValues', N'BooleanValue'),
    (N'DatasetRecordValues', N'DateTimeValue'),
    (N'ImportErrors', N'Id'),
    (N'ImportErrors', N'ImportId'),
    (N'ImportErrors', N'WorkspaceId'),
    (N'ImportErrors', N'RowNumber'),
    (N'ImportErrors', N'ColumnName'),
    (N'ImportErrors', N'InvalidValue'),
    (N'ImportErrors', N'ErrorType'),
    (N'ImportErrors', N'ValidationRule'),
    (N'ImportErrors', N'ErrorDescription'),
    (N'ImportErrors', N'ErrorTimestampUtc'),
    (N'DatasetTransformationConfigurations', N'Id'),
    (N'DatasetTransformationConfigurations', N'DatasetId'),
    (N'DatasetTransformationConfigurations', N'WorkspaceId'),
    (N'DatasetTransformationConfigurations', N'Version'),
    (N'DatasetTransformationConfigurations', N'IsActive'),
    (N'DatasetTransformationConfigurations', N'ConfigurationJson'),
    (N'DatasetTransformationConfigurations', N'CreatedByUserId'),
    (N'DatasetTransformationConfigurations', N'CreatedAtUtc');

SELECT
    RequiredColumn.TableName,
    RequiredColumn.ColumnName,
    N'Missing' AS ValidationStatus
FROM @RequiredIngestionColumns AS RequiredColumn
LEFT JOIN sys.tables AS TableRecord
    ON TableRecord.name = RequiredColumn.TableName
   AND SCHEMA_NAME(TableRecord.schema_id) = N'dbo'
LEFT JOIN sys.columns AS ColumnRecord
    ON ColumnRecord.object_id = TableRecord.object_id
   AND ColumnRecord.name = RequiredColumn.ColumnName
WHERE ColumnRecord.column_id IS NULL
ORDER BY RequiredColumn.TableName, RequiredColumn.ColumnName;
GO

------------------------------------------------------------
-- Critical Ingestion Index Verification
-- Expected: every index status is Available
------------------------------------------------------------
DECLARE @RequiredIngestionIndexes TABLE
(
    TableName SYSNAME NOT NULL,
    IndexName SYSNAME NOT NULL,
    ExpectedUnique BIT NOT NULL,
    PRIMARY KEY (TableName, IndexName)
);

INSERT INTO @RequiredIngestionIndexes (TableName, IndexName, ExpectedUnique)
VALUES
    (N'UploadedDataFiles', N'IX_UploadedDataFiles_DatasetId', 0),
    (N'UploadedDataFiles', N'IX_UploadedDataFiles_WorkspaceId_DatasetId', 0),
    (N'DatasetColumns', N'IX_DatasetColumns_DatasetId_NormalizedName', 1),
    (N'DataImports', N'IX_DataImports_DatasetId', 0),
    (N'DataImports', N'IX_DataImports_WorkspaceId_DatasetId_CreatedAtUtc', 0),
    (N'DataImports', N'IX_DataImports_DatasetId_Status', 0),
    (N'DataImports', N'IX_DataImports_FileId', 0),
    (N'DataImports', N'IX_DataImports_TransformationConfigurationId', 0),
    (N'ImportStagingRows', N'IX_ImportStagingRows_ImportId_RowNumber', 0),
    (N'ImportStagingValues', N'IX_ImportStagingValues_StagingRowId', 0),
    (N'DatasetRecords', N'IX_DatasetRecords_DatasetId_KeyHash', 0),
    (N'DatasetRecordValues', N'IX_DatasetRecordValues_DatasetRecordId_DatasetColumnId', 1),
    (N'DatasetRecordValues', N'IX_DatasetRecordValues_DatasetColumnId', 0),
    (N'ImportErrors', N'IX_ImportErrors_ImportId_ErrorTimestampUtc', 0),
    (N'DatasetTransformationConfigurations', N'IX_DatasetTransformationConfigurations_DatasetId_Version', 1),
    (N'DatasetTransformationConfigurations', N'IX_DatasetTransformationConfigurations_DatasetId_IsActive', 1);

SELECT
    RequiredIndex.TableName,
    RequiredIndex.IndexName,
    RequiredIndex.ExpectedUnique,
    ExistingIndex.is_unique AS ActualUnique,
    CASE
        WHEN ExistingIndex.index_id IS NULL THEN N'Missing'
        WHEN ExistingIndex.is_unique <> RequiredIndex.ExpectedUnique THEN N'Uniqueness mismatch'
        ELSE N'Available'
    END AS IndexStatus
FROM @RequiredIngestionIndexes AS RequiredIndex
LEFT JOIN sys.indexes AS ExistingIndex
    ON ExistingIndex.object_id = OBJECT_ID(N'dbo.' + RequiredIndex.TableName, N'U')
   AND ExistingIndex.name = RequiredIndex.IndexName
ORDER BY RequiredIndex.TableName, RequiredIndex.IndexName;
GO

------------------------------------------------------------
-- Ingestion Foreign-Key Verification
-- Expected: every foreign key status is Available
------------------------------------------------------------
DECLARE @RequiredIngestionForeignKeys TABLE
(
    TableName SYSNAME NOT NULL,
    ForeignKeyName SYSNAME NOT NULL,
    PRIMARY KEY (TableName, ForeignKeyName)
);

INSERT INTO @RequiredIngestionForeignKeys (TableName, ForeignKeyName)
VALUES
    (N'UploadedDataFiles', N'FK_UploadedDataFiles_Datasets_DatasetId'),
    (N'DatasetColumns', N'FK_DatasetColumns_Datasets_DatasetId'),
    (N'DataImports', N'FK_DataImports_Datasets_DatasetId'),
    (N'DataImports', N'FK_DataImports_UploadedDataFiles_FileId'),
    (N'DataImports', N'FK_DataImports_DatasetTransformationConfigurations_TransformationConfigurationId'),
    (N'ImportStagingRows', N'FK_ImportStagingRows_DataImports_ImportId'),
    (N'ImportStagingValues', N'FK_ImportStagingValues_ImportStagingRows_StagingRowId'),
    (N'DatasetRecords', N'FK_DatasetRecords_Datasets_DatasetId'),
    (N'DatasetRecordValues', N'FK_DatasetRecordValues_DatasetRecords_DatasetRecordId'),
    (N'DatasetRecordValues', N'FK_DatasetRecordValues_DatasetColumns_DatasetColumnId'),
    (N'ImportErrors', N'FK_ImportErrors_DataImports_ImportId'),
    (N'DatasetTransformationConfigurations', N'FK_DatasetTransformationConfigurations_Datasets_DatasetId');

SELECT
    RequiredForeignKey.TableName,
    RequiredForeignKey.ForeignKeyName,
    CASE WHEN ExistingForeignKey.object_id IS NULL THEN N'Missing' ELSE N'Available' END AS ForeignKeyStatus
FROM @RequiredIngestionForeignKeys AS RequiredForeignKey
LEFT JOIN sys.foreign_keys AS ExistingForeignKey
    ON ExistingForeignKey.parent_object_id = OBJECT_ID(N'dbo.' + RequiredForeignKey.TableName, N'U')
   AND ExistingForeignKey.name = RequiredForeignKey.ForeignKeyName
ORDER BY RequiredForeignKey.TableName, RequiredForeignKey.ForeignKeyName;
GO

------------------------------------------------------------
-- Import Permission Matrix
------------------------------------------------------------
SELECT
    RoleRecord.Name AS RoleName,
    PermissionRecord.Name AS PermissionName,
    PermissionRecord.Description
FROM dbo.RolePermissions AS Mapping
INNER JOIN dbo.AspNetRoles AS RoleRecord
    ON RoleRecord.Id = Mapping.RoleId
INNER JOIN dbo.Permissions AS PermissionRecord
    ON PermissionRecord.Id = Mapping.PermissionId
WHERE PermissionRecord.Name LIKE N'imports.%'
ORDER BY
    CASE RoleRecord.Name
        WHEN N'Platform Administrator' THEN 1
        WHEN N'Workspace Administrator' THEN 2
        WHEN N'Data Analyst' THEN 3
        WHEN N'Business User' THEN 4
        WHEN N'Viewer' THEN 5
        ELSE 99
    END,
    PermissionRecord.Name;
GO

------------------------------------------------------------
-- Uploaded File Metadata Validation
-- Only non-empty CSV/XLSX files up to 25 MB are supported.
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    FileRecord.Id,
    FileRecord.DatasetId,
    FileRecord.WorkspaceId,
    FileRecord.OriginalFileName,
    FileRecord.Extension,
    FileRecord.FileSizeBytes,
    CASE
        WHEN LOWER(FileRecord.Extension) NOT IN (N'.csv', N'.xlsx') THEN N'Unsupported file extension'
        WHEN FileRecord.FileSizeBytes <= 0 THEN N'File is empty'
        WHEN FileRecord.FileSizeBytes > 26214400 THEN N'File exceeds 25 MB'
        WHEN NULLIF(LTRIM(RTRIM(FileRecord.OriginalFileName)), N'') IS NULL THEN N'Original file name is missing'
        WHEN NULLIF(LTRIM(RTRIM(FileRecord.StoredFileName)), N'') IS NULL THEN N'Stored file name is missing'
        WHEN NULLIF(LTRIM(RTRIM(FileRecord.FilePath)), N'') IS NULL THEN N'File path is missing'
        WHEN FileRecord.WorkspaceId <> DatasetRecord.WorkspaceId THEN N'File and dataset workspaces differ'
        WHEN UploadUser.Id IS NULL THEN N'Uploading user does not exist'
    END AS ValidationIssue
FROM dbo.UploadedDataFiles AS FileRecord
INNER JOIN dbo.Datasets AS DatasetRecord
    ON DatasetRecord.Id = FileRecord.DatasetId
LEFT JOIN dbo.AspNetUsers AS UploadUser
    ON UploadUser.Id = FileRecord.UploadedByUserId
WHERE LOWER(FileRecord.Extension) NOT IN (N'.csv', N'.xlsx')
   OR FileRecord.FileSizeBytes <= 0
   OR FileRecord.FileSizeBytes > 26214400
   OR NULLIF(LTRIM(RTRIM(FileRecord.OriginalFileName)), N'') IS NULL
   OR NULLIF(LTRIM(RTRIM(FileRecord.StoredFileName)), N'') IS NULL
   OR NULLIF(LTRIM(RTRIM(FileRecord.FilePath)), N'') IS NULL
   OR FileRecord.WorkspaceId <> DatasetRecord.WorkspaceId
   OR UploadUser.Id IS NULL;
GO

------------------------------------------------------------
-- Inferred Dataset Schema Validation
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    ColumnRecord.Id,
    ColumnRecord.DatasetId,
    ColumnRecord.WorkspaceId,
    ColumnRecord.Name,
    ColumnRecord.DataType,
    ColumnRecord.Ordinal,
    CASE
        WHEN NULLIF(LTRIM(RTRIM(ColumnRecord.Name)), N'') IS NULL THEN N'Column name is missing'
        WHEN NULLIF(LTRIM(RTRIM(ColumnRecord.NormalizedName)), N'') IS NULL THEN N'Normalized column name is missing'
        WHEN ColumnRecord.DataType NOT IN (N'String', N'Integer', N'Decimal', N'Boolean', N'DateTime') THEN N'Unsupported inferred data type'
        WHEN ColumnRecord.Ordinal < 0 THEN N'Column ordinal cannot be negative'
        WHEN ColumnRecord.WorkspaceId <> DatasetRecord.WorkspaceId THEN N'Column and dataset workspaces differ'
    END AS ValidationIssue
FROM dbo.DatasetColumns AS ColumnRecord
INNER JOIN dbo.Datasets AS DatasetRecord
    ON DatasetRecord.Id = ColumnRecord.DatasetId
WHERE NULLIF(LTRIM(RTRIM(ColumnRecord.Name)), N'') IS NULL
   OR NULLIF(LTRIM(RTRIM(ColumnRecord.NormalizedName)), N'') IS NULL
   OR ColumnRecord.DataType NOT IN (N'String', N'Integer', N'Decimal', N'Boolean', N'DateTime')
   OR ColumnRecord.Ordinal < 0
   OR ColumnRecord.WorkspaceId <> DatasetRecord.WorkspaceId;
GO

------------------------------------------------------------
-- Import Configuration and Lifecycle Validation
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    ImportRecord.Id,
    ImportRecord.DatasetId,
    ImportRecord.Status,
    ImportRecord.ImportMode,
    ImportRecord.DuplicateBehavior,
    ImportRecord.CreatedAtUtc,
    ImportRecord.QueuedAtUtc,
    ImportRecord.StartedAtUtc,
    ImportRecord.CompletedAtUtc,
    CASE
        WHEN ImportRecord.Status NOT IN
             (N'Created', N'Queued', N'Processing', N'Completed', N'Completed With Errors', N'Failed', N'Cancelled')
            THEN N'Unsupported import status'
        WHEN ImportRecord.ImportMode NOT IN (N'Full', N'Append') THEN N'Unsupported import mode'
        WHEN ImportRecord.DuplicateBehavior NOT IN (N'Skip', N'Reject', N'Update') THEN N'Unsupported duplicate behavior'
        WHEN ImportRecord.InvalidRecordBehavior <> N'Skip' THEN N'Unsupported invalid-record behavior'
        WHEN DATALENGTH(ImportRecord.CsvDelimiter) <> 2 THEN N'CSV delimiter must contain one character'
        WHEN ISJSON(ImportRecord.KeyColumnsJson) <> 1 THEN N'KeyColumnsJson is not valid JSON'
        WHEN ImportRecord.DatasetId <> FileRecord.DatasetId THEN N'Import file belongs to another dataset'
        WHEN ImportRecord.WorkspaceId <> FileRecord.WorkspaceId
          OR ImportRecord.WorkspaceId <> DatasetRecord.WorkspaceId THEN N'Import workspace is inconsistent'
        WHEN LOWER(FileRecord.Extension) = N'.xlsx'
         AND NULLIF(LTRIM(RTRIM(ImportRecord.WorksheetName)), N'') IS NULL THEN N'Worksheet is required for XLSX'
        WHEN ImportRecord.TotalRecords < 0
          OR ImportRecord.SuccessfullyImportedRecords < 0
          OR ImportRecord.RejectedRecords < 0
          OR ImportRecord.ErrorCount < 0 THEN N'Import counters cannot be negative'
        WHEN ImportRecord.Status IN (N'Queued', N'Processing')
         AND ImportRecord.QueuedAtUtc IS NULL THEN N'Active import has no queue timestamp'
        WHEN ImportRecord.Status = N'Processing'
         AND ImportRecord.StartedAtUtc IS NULL THEN N'Processing import has no start timestamp'
        WHEN ImportRecord.Status IN (N'Completed', N'Completed With Errors', N'Failed', N'Cancelled')
         AND ImportRecord.CompletedAtUtc IS NULL THEN N'Terminal import has no completion timestamp'
        WHEN ImportRecord.CompletedAtUtc < ImportRecord.CreatedAtUtc THEN N'Completion precedes creation'
    END AS ValidationIssue
FROM dbo.DataImports AS ImportRecord
INNER JOIN dbo.UploadedDataFiles AS FileRecord
    ON FileRecord.Id = ImportRecord.FileId
INNER JOIN dbo.Datasets AS DatasetRecord
    ON DatasetRecord.Id = ImportRecord.DatasetId
WHERE ImportRecord.Status NOT IN
      (N'Created', N'Queued', N'Processing', N'Completed', N'Completed With Errors', N'Failed', N'Cancelled')
   OR ImportRecord.ImportMode NOT IN (N'Full', N'Append')
   OR ImportRecord.DuplicateBehavior NOT IN (N'Skip', N'Reject', N'Update')
   OR ImportRecord.InvalidRecordBehavior <> N'Skip'
   OR DATALENGTH(ImportRecord.CsvDelimiter) <> 2
   OR ISJSON(ImportRecord.KeyColumnsJson) <> 1
   OR ImportRecord.DatasetId <> FileRecord.DatasetId
   OR ImportRecord.WorkspaceId <> FileRecord.WorkspaceId
   OR ImportRecord.WorkspaceId <> DatasetRecord.WorkspaceId
   OR (LOWER(FileRecord.Extension) = N'.xlsx'
       AND NULLIF(LTRIM(RTRIM(ImportRecord.WorksheetName)), N'') IS NULL)
   OR ImportRecord.TotalRecords < 0
   OR ImportRecord.SuccessfullyImportedRecords < 0
   OR ImportRecord.RejectedRecords < 0
   OR ImportRecord.ErrorCount < 0
   OR (ImportRecord.Status IN (N'Queued', N'Processing') AND ImportRecord.QueuedAtUtc IS NULL)
   OR (ImportRecord.Status = N'Processing' AND ImportRecord.StartedAtUtc IS NULL)
   OR (ImportRecord.Status IN (N'Completed', N'Completed With Errors', N'Failed', N'Cancelled')
       AND ImportRecord.CompletedAtUtc IS NULL)
   OR ImportRecord.CompletedAtUtc < ImportRecord.CreatedAtUtc;
GO

------------------------------------------------------------
-- One Active Import Per Dataset
-- Serializable application-side start logic must prevent duplicates.
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    WorkspaceId,
    DatasetId,
    COUNT(*) AS ActiveImportCount
FROM dbo.DataImports
WHERE Status IN (N'Queued', N'Processing')
GROUP BY WorkspaceId, DatasetId
HAVING COUNT(*) > 1;
GO

------------------------------------------------------------
-- Workspace Isolation Across the Ingestion Graph
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    ValidationRecord.EntityType,
    ValidationRecord.EntityId,
    ValidationRecord.ValidationIssue
FROM
(
    SELECT
        N'UploadedDataFile' AS EntityType,
        CONVERT(NVARCHAR(36), FileRecord.Id) AS EntityId,
        N'Uploaded file workspace differs from dataset workspace' AS ValidationIssue
    FROM dbo.UploadedDataFiles AS FileRecord
    INNER JOIN dbo.Datasets AS DatasetRecord
        ON DatasetRecord.Id = FileRecord.DatasetId
    WHERE FileRecord.WorkspaceId <> DatasetRecord.WorkspaceId

    UNION ALL

    SELECT
        N'DatasetColumn',
        CONVERT(NVARCHAR(36), ColumnRecord.Id),
        N'Dataset column workspace differs from dataset workspace'
    FROM dbo.DatasetColumns AS ColumnRecord
    INNER JOIN dbo.Datasets AS DatasetRecord
        ON DatasetRecord.Id = ColumnRecord.DatasetId
    WHERE ColumnRecord.WorkspaceId <> DatasetRecord.WorkspaceId

    UNION ALL

    SELECT
        N'DataImport',
        CONVERT(NVARCHAR(36), ImportRecord.Id),
        N'Import, file and dataset do not share the same workspace and dataset'
    FROM dbo.DataImports AS ImportRecord
    INNER JOIN dbo.UploadedDataFiles AS FileRecord
        ON FileRecord.Id = ImportRecord.FileId
    INNER JOIN dbo.Datasets AS DatasetRecord
        ON DatasetRecord.Id = ImportRecord.DatasetId
    WHERE ImportRecord.DatasetId <> FileRecord.DatasetId
       OR ImportRecord.WorkspaceId <> FileRecord.WorkspaceId
       OR ImportRecord.WorkspaceId <> DatasetRecord.WorkspaceId

    UNION ALL

    SELECT
        N'ImportStagingRow',
        CONVERT(NVARCHAR(36), StagingRow.Id),
        N'Staging row workspace differs from import workspace'
    FROM dbo.ImportStagingRows AS StagingRow
    INNER JOIN dbo.DataImports AS ImportRecord
        ON ImportRecord.Id = StagingRow.ImportId
    WHERE StagingRow.WorkspaceId <> ImportRecord.WorkspaceId

    UNION ALL

    SELECT
        N'DatasetRecord',
        CONVERT(NVARCHAR(36), RecordEntity.Id),
        N'Dataset record is inconsistent with its dataset or source import'
    FROM dbo.DatasetRecords AS RecordEntity
    INNER JOIN dbo.Datasets AS DatasetRecord
        ON DatasetRecord.Id = RecordEntity.DatasetId
    LEFT JOIN dbo.DataImports AS SourceImport
        ON SourceImport.Id = RecordEntity.SourceImportId
    WHERE RecordEntity.WorkspaceId <> DatasetRecord.WorkspaceId
       OR SourceImport.Id IS NULL
       OR SourceImport.DatasetId <> RecordEntity.DatasetId
       OR SourceImport.WorkspaceId <> RecordEntity.WorkspaceId

    UNION ALL

    SELECT
        N'DatasetRecordValue',
        CONVERT(NVARCHAR(36), RecordValue.Id),
        N'Record value references a column from another dataset or workspace'
    FROM dbo.DatasetRecordValues AS RecordValue
    INNER JOIN dbo.DatasetRecords AS RecordEntity
        ON RecordEntity.Id = RecordValue.DatasetRecordId
    INNER JOIN dbo.DatasetColumns AS ColumnRecord
        ON ColumnRecord.Id = RecordValue.DatasetColumnId
    WHERE RecordEntity.DatasetId <> ColumnRecord.DatasetId
       OR RecordEntity.WorkspaceId <> ColumnRecord.WorkspaceId

    UNION ALL

    SELECT
        N'ImportError',
        CONVERT(NVARCHAR(36), ErrorRecord.Id),
        N'Import error workspace differs from import workspace'
    FROM dbo.ImportErrors AS ErrorRecord
    INNER JOIN dbo.DataImports AS ImportRecord
        ON ImportRecord.Id = ErrorRecord.ImportId
    WHERE ErrorRecord.WorkspaceId <> ImportRecord.WorkspaceId
) AS ValidationRecord
ORDER BY ValidationRecord.EntityType, ValidationRecord.EntityId;
GO

------------------------------------------------------------
-- Typed Dataset Value Validation
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    RecordValue.Id,
    RecordValue.DatasetRecordId,
    ColumnRecord.Name AS ColumnName,
    ColumnRecord.DataType,
    RecordValue.RawValue
FROM dbo.DatasetRecordValues AS RecordValue
INNER JOIN dbo.DatasetColumns AS ColumnRecord
    ON ColumnRecord.Id = RecordValue.DatasetColumnId
WHERE RecordValue.RawValue IS NOT NULL
  AND LTRIM(RTRIM(RecordValue.RawValue)) <> N''
  AND
  (
      (ColumnRecord.DataType = N'String'
       AND (RecordValue.StringValue IS NULL
            OR RecordValue.IntegerValue IS NOT NULL
            OR RecordValue.DecimalValue IS NOT NULL
            OR RecordValue.BooleanValue IS NOT NULL
            OR RecordValue.DateTimeValue IS NOT NULL))
   OR (ColumnRecord.DataType = N'Integer'
       AND (RecordValue.IntegerValue IS NULL
            OR RecordValue.StringValue IS NOT NULL
            OR RecordValue.DecimalValue IS NOT NULL
            OR RecordValue.BooleanValue IS NOT NULL
            OR RecordValue.DateTimeValue IS NOT NULL))
   OR (ColumnRecord.DataType = N'Decimal'
       AND (RecordValue.DecimalValue IS NULL
            OR RecordValue.StringValue IS NOT NULL
            OR RecordValue.IntegerValue IS NOT NULL
            OR RecordValue.BooleanValue IS NOT NULL
            OR RecordValue.DateTimeValue IS NOT NULL))
   OR (ColumnRecord.DataType = N'Boolean'
       AND (RecordValue.BooleanValue IS NULL
            OR RecordValue.StringValue IS NOT NULL
            OR RecordValue.IntegerValue IS NOT NULL
            OR RecordValue.DecimalValue IS NOT NULL
            OR RecordValue.DateTimeValue IS NOT NULL))
   OR (ColumnRecord.DataType = N'DateTime'
       AND (RecordValue.DateTimeValue IS NULL
            OR RecordValue.StringValue IS NOT NULL
            OR RecordValue.IntegerValue IS NOT NULL
            OR RecordValue.DecimalValue IS NOT NULL
            OR RecordValue.BooleanValue IS NOT NULL))
  );
GO

------------------------------------------------------------
-- Import History with Filtering and Pagination
------------------------------------------------------------
DECLARE @ImportDatasetId UNIQUEIDENTIFIER = NULL;
DECLARE @ImportStatus NVARCHAR(50) = NULL;
DECLARE @ImportPage INT = 1;
DECLARE @ImportPageSize INT = 20;

SET @ImportPage = CASE WHEN @ImportPage < 1 THEN 1 ELSE @ImportPage END;
SET @ImportPageSize = CASE
    WHEN @ImportPageSize < 1 THEN 20
    WHEN @ImportPageSize > 100 THEN 100
    ELSE @ImportPageSize
END;

SELECT
    ImportRecord.Id AS ImportId,
    ImportRecord.DatasetId,
    ImportRecord.WorkspaceId,
    FileRecord.OriginalFileName,
    ImportRecord.ImportMode,
    ImportRecord.DuplicateBehavior,
    ImportRecord.Status,
    ImportRecord.TotalRecords,
    ImportRecord.SuccessfullyImportedRecords,
    ImportRecord.RejectedRecords,
    ImportRecord.ErrorCount,
    ImportRecord.InitiatedByUserId,
    ImportRecord.CreatedAtUtc,
    ImportRecord.StartedAtUtc,
    ImportRecord.CompletedAtUtc,
    ImportRecord.FailureMessage
FROM dbo.DataImports AS ImportRecord
INNER JOIN dbo.UploadedDataFiles AS FileRecord
    ON FileRecord.Id = ImportRecord.FileId
WHERE (@ImportDatasetId IS NULL OR ImportRecord.DatasetId = @ImportDatasetId)
  AND (@ImportStatus IS NULL OR ImportRecord.Status = @ImportStatus)
ORDER BY ImportRecord.CreatedAtUtc DESC
OFFSET (@ImportPage - 1) * @ImportPageSize ROWS
FETCH NEXT @ImportPageSize ROWS ONLY;
GO

------------------------------------------------------------
-- Persistent Row-Level Import Errors
------------------------------------------------------------
SELECT TOP (200)
    ErrorRecord.Id,
    ErrorRecord.ImportId,
    ErrorRecord.WorkspaceId,
    ErrorRecord.RowNumber,
    ErrorRecord.ColumnName,
    ErrorRecord.InvalidValue,
    ErrorRecord.ErrorDescription,
    ErrorRecord.ErrorTimestampUtc
FROM dbo.ImportErrors AS ErrorRecord
ORDER BY ErrorRecord.ErrorTimestampUtc DESC, ErrorRecord.RowNumber;
GO

------------------------------------------------------------
-- Import Lifecycle, Mode and Duplicate-Handling Summary
------------------------------------------------------------
SELECT
    Status,
    ImportMode,
    DuplicateBehavior,
    COUNT(*) AS ImportCount,
    SUM(TotalRecords) AS TotalRecordCount,
    SUM(SuccessfullyImportedRecords) AS SuccessfullyImportedRecordCount,
    SUM(RejectedRecords) AS RejectedRecordCount,
    SUM(ErrorCount) AS ErrorCount
FROM dbo.DataImports
GROUP BY Status, ImportMode, DuplicateBehavior
ORDER BY Status, ImportMode, DuplicateBehavior;
GO

------------------------------------------------------------
-- Cancellation Consistency
-- Expected: 0 rows
------------------------------------------------------------
SELECT
    Id,
    Status,
    CancellationRequested,
    CompletedAtUtc
FROM dbo.DataImports
WHERE (Status = N'Cancelled'
       AND (CancellationRequested = 0 OR CompletedAtUtc IS NULL))
   OR (CancellationRequested = 1
       AND Status NOT IN (N'Queued', N'Processing', N'Cancelled'));
GO

------------------------------------------------------------
-- Import Audit Verification
------------------------------------------------------------
SELECT TOP (100)
    Id,
    UserId,
    WorkspaceId,
    Action,
    EntityType,
    EntityId,
    Details,
    IpAddress,
    CreatedAtUtc
FROM dbo.AuditLogs
WHERE EntityType = N'DataImport'
   OR Action IN
      (N'File Upload', N'Import Configuration', N'Import Initiation',
       N'Import Completion', N'Import Failure', N'Import Cancellation')
ORDER BY CreatedAtUtc DESC;
GO

------------------------------------------------------------
-- Final Import and Ingestion Verification Summary
------------------------------------------------------------
SELECT
    (SELECT COUNT(*) FROM dbo.UploadedDataFiles) AS UploadedFileCount,
    (SELECT COUNT(*) FROM dbo.DatasetColumns) AS DatasetColumnCount,
    (SELECT COUNT(*) FROM dbo.DataImports) AS ImportCount,
    (SELECT COUNT(*) FROM dbo.DataImports WHERE Status IN (N'Queued', N'Processing')) AS ActiveImportCount,
    (SELECT COUNT(*) FROM dbo.DataImports WHERE Status = N'Completed') AS CompletedImportCount,
    (SELECT COUNT(*) FROM dbo.DataImports WHERE Status = N'Completed With Errors') AS CompletedWithErrorsCount,
    (SELECT COUNT(*) FROM dbo.DataImports WHERE Status = N'Failed') AS FailedImportCount,
    (SELECT COUNT(*) FROM dbo.DataImports WHERE Status = N'Cancelled') AS CancelledImportCount,
    (SELECT COUNT(*) FROM dbo.ImportStagingRows) AS StagingRowCount,
    (SELECT COUNT(*) FROM dbo.DatasetRecords) AS DatasetRecordCount,
    (SELECT COUNT(*) FROM dbo.DatasetRecordValues) AS DatasetRecordValueCount,
    (SELECT COUNT(*) FROM dbo.ImportErrors) AS ImportErrorCount,
    (SELECT COUNT(*) FROM dbo.DatasetTransformationConfigurations) AS TransformationConfigurationCount,
    (SELECT COUNT(*) FROM dbo.DatasetTransformationConfigurations WHERE IsActive = 1) AS ActiveTransformationConfigurationCount,
    (SELECT COUNT(*) FROM dbo.DataImports WHERE TransformationConfigurationId IS NOT NULL) AS PinnedImportCount,
    (SELECT COUNT(*) FROM dbo.Permissions WHERE Name LIKE N'imports.%') AS ImportPermissionCount;
GO

/*
==============================================================================
IMPLEMENTATION ASSUMPTIONS DOCUMENTED BY THIS SQL SCRIPT
==============================================================================

1. Dataset Code is globally unique and uses DS-000001 style values.
2. Dataset Code generation is performed by the application using
   dbo.DatasetCodeSequence; the database enforces uniqueness with IX_Datasets_Code.
3. Dataset lifecycle Status is limited to Draft, Active and Archived.
4. Soft delete uses IsDeleted / DeletedAtUtc / DeletedByUserId and is not a Status.
5. Version 1 is created when a dataset is registered. Metadata changes create a
   new immutable snapshot and increment Datasets.CurrentVersion.
6. Lifecycle-only changes do not create a metadata version unless metadata changes.
7. Historical versions are read-only; datasets.versions.restore is intentionally absent.
8. Dataset workspace assignment is immutable after creation.
9. Data Analyst may create/update and ingest only datasets assigned to that user
   as Owner. The service layer enforces the ownership rule.
10. Workspace Administrator access is limited to the administrator's workspace.
11. Business User and Viewer have read-only dataset and import-history access.
12. Dataset source fields are descriptive metadata. Uploaded file metadata and
    ingested values are stored in dedicated ingestion tables.
13. Upload, import configuration and import execution are separate secured steps.
14. Only non-empty CSV and XLSX files up to 25 MB are accepted. XLSX imports
    require an existing worksheet name; CSV imports accept a one-character delimiter.
15. Import lifecycle is Created, Queued, Processing and one of Completed,
    Completed With Errors, Failed or Cancelled.
16. Only one Queued or Processing import is permitted for a dataset. The
    application enforces this with a serializable transaction when starting an import.
17. Full replaces the dataset's typed records; Append preserves existing records.
18. Duplicate behavior is Skip, Reject or Update and may use composite key columns.
19. The first successful import must be Full and establishes the inferred schema.
    Supported inferred types are String, Integer, Decimal, Boolean and DateTime.
20. Parsed rows are staged before final writes. Invalid rows and duplicate
    rejections are retained as persistent row-level errors.
21. Final dataset-record writes are transactional. Stored typed values retain the
    original RawValue for traceability.
22. Workspace identity is propagated through uploads, schemas, imports, staging,
    records and errors; application query filters prevent cross-workspace access.
23. Imports run through a hosted background queue. Cancellation is cooperative for
    processing jobs and immediate for queued jobs.
24. Upload, configuration, start, completion, failure and cancellation actions are
    written to AuditLogs.
25. Dataset search defaults to page size 20, maximum 100 and UpdatedAtUtc descending.
    Import history uses the same page limits; import-error pages allow up to 200 rows.
26. EF Core migrations remain the primary schema mechanism. When the base schema
    through Task 17 exists, this script can also apply/repair the exact Task 18
    objects and synchronize its __EFMigrationsHistory entry.
27. Task 18 stores each immutable transformation/mapping/validation version as JSON
    in DatasetTransformationConfigurations.ConfigurationJson.
28. DatasetId + Version is unique and only one IsActive row is permitted per dataset.
29. DataImports captures TransformationConfigurationId and
    TransformationConfigurationVersion when the import is created.
30. Import processing loads the pinned configuration so activating a newer version
    does not change an already-created import.
31. ImportStagingValues retains OriginalValue and TransformedValue for preview and
    diagnostics; ImportErrors records ErrorType and ValidationRule context.
32. Task 18 reuses datasets.update, datasets.view and imports.view permissions, so
    no additional permission rows are required.
==============================================================================
*/
GO
