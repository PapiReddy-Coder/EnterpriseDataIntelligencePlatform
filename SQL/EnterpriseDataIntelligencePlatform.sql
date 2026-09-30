/*
==============================================================================
Enterprise Data Intelligence Platform
Consolidated Database Seed Upgrade and Verification Script
==============================================================================

Purpose:
- Includes the governance and access-policy upgrade exactly once; do not
  append or execute the standalone governance script separately.
- Adds dataset classification, stewardship, certification history, access
  requests, grants and access history, with existing RBAC permission mappings.
- Existing datasets receive Internal classification and Draft governance status.
- Verifies authentication, workspace, user, role, permission, refresh-token,
  audit-log, leave-management, dataset catalog, versioning, metadata and
  data-ingestion tables.
- Seeds and validates predefined roles, permissions and role-permission mappings.
- Verifies globally unique Dataset Codes, categories, tags, metadata versions,
  search/filter behavior, soft delete, RBAC and workspace isolation.
- Verifies secured CSV/XLSX upload metadata, inferred schemas, import lifecycle,
  Full/Append modes, duplicate handling, staging data, typed records, row-level
  errors, import history, cancellation and audit coverage.

Important:
- The database schema is managed using Entity Framework Core migrations.
- Apply all migrations before executing this script:

      Update-Database

  or:

      dotnet ef database update

- Application users must be created through ASP.NET Core Identity UserManager.
- The script is designed for repeatable local setup and verification.
- Back up the target database before execution. This is not a complete
  empty-database installer: earlier application schema requires EF migrations.
- The governance upgrade is guarded by its EF migration-history entry.
  An already applied migration is skipped; inconsistent/partial schema is
  not automatically repaired. Stop on errors and investigate before retrying.
- This file selects EnterpriseDataIntelligencePlatform below. Confirm that
  database name before execution. API authorization remains application code.

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
- UploadedDataFiles
- DatasetColumns
- DataImports
- ImportStagingRows
- ImportStagingValues
- DatasetRecords
- DatasetRecordValues
- ImportErrors
- DatasetGovernance
- DatasetCertifications
- DatasetAccessRequests
- DatasetAccessGrants
- DatasetAccessHistory
- __EFMigrationsHistory

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
-- Verify Required Tables Exist
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
    (N'ImportErrors', N'ErrorDescription'),
    (N'ImportErrors', N'ErrorTimestampUtc');

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
    (N'ImportStagingRows', N'IX_ImportStagingRows_ImportId_RowNumber', 0),
    (N'ImportStagingValues', N'IX_ImportStagingValues_StagingRowId', 0),
    (N'DatasetRecords', N'IX_DatasetRecords_DatasetId_KeyHash', 0),
    (N'DatasetRecordValues', N'IX_DatasetRecordValues_DatasetRecordId_DatasetColumnId', 1),
    (N'DatasetRecordValues', N'IX_DatasetRecordValues_DatasetColumnId', 0),
    (N'ImportErrors', N'IX_ImportErrors_ImportId_ErrorTimestampUtc', 0);

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
    (N'ImportStagingRows', N'FK_ImportStagingRows_DataImports_ImportId'),
    (N'ImportStagingValues', N'FK_ImportStagingValues_ImportStagingRows_StagingRowId'),
    (N'DatasetRecords', N'FK_DatasetRecords_Datasets_DatasetId'),
    (N'DatasetRecordValues', N'FK_DatasetRecordValues_DatasetRecords_DatasetRecordId'),
    (N'DatasetRecordValues', N'FK_DatasetRecordValues_DatasetColumns_DatasetColumnId'),
    (N'ImportErrors', N'FK_ImportErrors_DataImports_ImportId');

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
26. EF Core migrations own the schema. This script is an idempotent seed and
    verification companion and must be executed only after all migrations succeed.
==============================================================================
*/
GO
/* Data transformation, mapping and validation module */
IF OBJECT_ID(N'dbo.DatasetTransformationConfigurations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DatasetTransformationConfigurations
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DatasetTransformationConfigurations PRIMARY KEY,
        DatasetId uniqueidentifier NOT NULL,
        WorkspaceId uniqueidentifier NOT NULL,
        Version int NOT NULL,
        IsActive bit NOT NULL,
        ConfigurationJson nvarchar(max) NOT NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2 NOT NULL,
        CONSTRAINT FK_DatasetTransformationConfigurations_Datasets FOREIGN KEY (DatasetId) REFERENCES dbo.Datasets(Id)
    );
    CREATE UNIQUE INDEX UX_DatasetTransformationConfigurations_Dataset_Version ON dbo.DatasetTransformationConfigurations(DatasetId, Version);
    CREATE UNIQUE INDEX UX_DatasetTransformationConfigurations_Active ON dbo.DatasetTransformationConfigurations(DatasetId, IsActive) WHERE IsActive = 1;
END;
IF COL_LENGTH(N'dbo.DataImports', N'TransformationConfigurationId') IS NULL
BEGIN
    ALTER TABLE dbo.DataImports ADD TransformationConfigurationId uniqueidentifier NULL, TransformationConfigurationVersion int NULL;
    ALTER TABLE dbo.DataImports ADD CONSTRAINT FK_DataImports_TransformationConfiguration FOREIGN KEY (TransformationConfigurationId) REFERENCES dbo.DatasetTransformationConfigurations(Id);
    CREATE INDEX IX_DataImports_TransformationConfigurationId ON dbo.DataImports(TransformationConfigurationId);
END;
IF COL_LENGTH(N'dbo.ImportErrors', N'ErrorType') IS NULL
    ALTER TABLE dbo.ImportErrors ADD ErrorType nvarchar(50) NOT NULL CONSTRAINT DF_ImportErrors_ErrorType DEFAULT N'Processing Error';
IF COL_LENGTH(N'dbo.ImportErrors', N'ValidationRule') IS NULL
    ALTER TABLE dbo.ImportErrors ADD ValidationRule nvarchar(100) NULL;
IF COL_LENGTH(N'dbo.ImportStagingValues', N'OriginalValue') IS NULL
    ALTER TABLE dbo.ImportStagingValues ADD OriginalValue nvarchar(4000) NULL;
IF COL_LENGTH(N'dbo.ImportStagingValues', N'TransformedValue') IS NULL
    ALTER TABLE dbo.ImportStagingValues ADD TransformedValue nvarchar(4000) NULL;

GO

/*
==============================================================================
Data Quality Profiling and Monitoring
==============================================================================
*/

IF OBJECT_ID(N'dbo.DataQualityProfileRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DataQualityProfileRuns
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DataQualityProfileRuns PRIMARY KEY,
        DatasetId uniqueidentifier NOT NULL,
        WorkspaceId uniqueidentifier NOT NULL,
        ImportId uniqueidentifier NULL,
        RequestedByUserId uniqueidentifier NULL,
        TriggerType nvarchar(20) NOT NULL,
        Status nvarchar(30) NOT NULL,
        RetryCount int NOT NULL CONSTRAINT DF_DataQualityProfileRuns_RetryCount DEFAULT 0,
        CancellationRequested bit NOT NULL CONSTRAINT DF_DataQualityProfileRuns_Cancel DEFAULT 0,
        TotalRecords int NOT NULL CONSTRAINT DF_DataQualityProfileRuns_Total DEFAULT 0,
        CompletenessScore decimal(5,2) NULL,
        ValidityScore decimal(5,2) NULL,
        UniquenessScore decimal(5,2) NULL,
        ConsistencyScore decimal(5,2) NULL,
        ErrorRate decimal(5,2) NULL,
        OverallQualityScore decimal(5,2) NULL,
        AppliedCompletenessThreshold decimal(5,2) NOT NULL,
        AppliedValidityThreshold decimal(5,2) NOT NULL,
        AppliedUniquenessThreshold decimal(5,2) NOT NULL,
        AppliedConsistencyThreshold decimal(5,2) NOT NULL,
        AppliedOverallThreshold decimal(5,2) NOT NULL,
        ThresholdStatus nvarchar(20) NOT NULL,
        ScoringVersion nvarchar(20) NOT NULL,
        FailureMessage nvarchar(4000) NULL,
        CreatedAtUtc datetime2 NOT NULL,
        QueuedAtUtc datetime2 NOT NULL,
        StartedAtUtc datetime2 NULL,
        CompletedAtUtc datetime2 NULL,
        CONSTRAINT FK_DataQualityProfileRuns_Datasets
            FOREIGN KEY (DatasetId) REFERENCES dbo.Datasets(Id),
        CONSTRAINT FK_DataQualityProfileRuns_DataImports
            FOREIGN KEY (ImportId) REFERENCES dbo.DataImports(Id)
    );

    CREATE INDEX IX_DataQualityProfileRuns_Workspace_Dataset_Created
        ON dbo.DataQualityProfileRuns(WorkspaceId, DatasetId, CreatedAtUtc);
    CREATE INDEX IX_DataQualityProfileRuns_Dataset_Status
        ON dbo.DataQualityProfileRuns(DatasetId, Status);
    CREATE INDEX IX_DataQualityProfileRuns_ImportId
        ON dbo.DataQualityProfileRuns(ImportId);
END;

IF OBJECT_ID(N'dbo.DataQualityThresholds', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DataQualityThresholds
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DataQualityThresholds PRIMARY KEY,
        DatasetId uniqueidentifier NOT NULL,
        WorkspaceId uniqueidentifier NOT NULL,
        MinimumCompleteness decimal(5,2) NOT NULL,
        MinimumValidity decimal(5,2) NOT NULL,
        MinimumUniqueness decimal(5,2) NOT NULL,
        MinimumConsistency decimal(5,2) NOT NULL,
        MinimumOverallScore decimal(5,2) NOT NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        UpdatedByUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2 NOT NULL,
        UpdatedAtUtc datetime2 NOT NULL,
        CONSTRAINT FK_DataQualityThresholds_Datasets
            FOREIGN KEY (DatasetId) REFERENCES dbo.Datasets(Id) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX UX_DataQualityThresholds_DatasetId
        ON dbo.DataQualityThresholds(DatasetId);
    CREATE INDEX IX_DataQualityThresholds_Workspace_Dataset
        ON dbo.DataQualityThresholds(WorkspaceId, DatasetId);
END;

IF OBJECT_ID(N'dbo.DataQualityColumnMetrics', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DataQualityColumnMetrics
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DataQualityColumnMetrics PRIMARY KEY,
        ProfileRunId uniqueidentifier NOT NULL,
        DatasetColumnId uniqueidentifier NOT NULL,
        WorkspaceId uniqueidentifier NOT NULL,
        ColumnName nvarchar(200) NOT NULL,
        DataType nvarchar(30) NOT NULL,
        IsRequired bit NOT NULL,
        IsKey bit NOT NULL,
        TotalRecords int NOT NULL,
        NonNullRecords int NOT NULL,
        NullCount int NOT NULL,
        NullPercentage decimal(5,2) NULL,
        DistinctCount int NOT NULL,
        UniqueCount int NOT NULL,
        DuplicateCount int NOT NULL,
        InvalidCount int NOT NULL,
        CompletenessScore decimal(5,2) NULL,
        ValidityScore decimal(5,2) NULL,
        UniquenessScore decimal(5,2) NULL,
        ConsistencyScore decimal(5,2) NULL,
        QualityScore decimal(5,2) NULL,
        MinimumValue nvarchar(4000) NULL,
        MaximumValue nvarchar(4000) NULL,
        AverageValue decimal(38,10) NULL,
        CONSTRAINT FK_DataQualityColumnMetrics_ProfileRuns
            FOREIGN KEY (ProfileRunId) REFERENCES dbo.DataQualityProfileRuns(Id) ON DELETE CASCADE,
        CONSTRAINT FK_DataQualityColumnMetrics_DatasetColumns
            FOREIGN KEY (DatasetColumnId) REFERENCES dbo.DatasetColumns(Id)
    );

    CREATE UNIQUE INDEX UX_DataQualityColumnMetrics_Profile_Column
        ON dbo.DataQualityColumnMetrics(ProfileRunId, DatasetColumnId);
    CREATE INDEX IX_DataQualityColumnMetrics_Workspace_Profile
        ON dbo.DataQualityColumnMetrics(WorkspaceId, ProfileRunId);
END;

IF OBJECT_ID(N'dbo.DataQualityIssues', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DataQualityIssues
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DataQualityIssues PRIMARY KEY,
        ProfileRunId uniqueidentifier NOT NULL,
        DatasetId uniqueidentifier NOT NULL,
        WorkspaceId uniqueidentifier NOT NULL,
        ImportId uniqueidentifier NULL,
        DatasetRecordId uniqueidentifier NULL,
        DatasetColumnId uniqueidentifier NULL,
        ColumnName nvarchar(200) NULL,
        IssueType nvarchar(50) NOT NULL,
        Severity nvarchar(20) NOT NULL,
        ValidationRule nvarchar(100) NULL,
        Description nvarchar(2000) NOT NULL,
        InvalidValue nvarchar(1000) NULL,
        CreatedAtUtc datetime2 NOT NULL,
        CONSTRAINT FK_DataQualityIssues_ProfileRuns
            FOREIGN KEY (ProfileRunId) REFERENCES dbo.DataQualityProfileRuns(Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_DataQualityIssues_Profile_Type
        ON dbo.DataQualityIssues(ProfileRunId, IssueType);
    CREATE INDEX IX_DataQualityIssues_Workspace_Dataset_Created
        ON dbo.DataQualityIssues(WorkspaceId, DatasetId, CreatedAtUtc);
END;
GO

------------------------------------------------------------
-- Data Quality Permissions
------------------------------------------------------------
DECLARE @DataQualityPermissions TABLE
(
    Id uniqueidentifier,
    Name nvarchar(150),
    Description nvarchar(500)
);

INSERT INTO @DataQualityPermissions (Id, Name, Description)
VALUES
    ('10000000-0000-0000-0000-000000000035', N'quality.profiles.run', N'Run data quality profiles'),
    ('10000000-0000-0000-0000-000000000036', N'quality.profiles.view', N'View data quality profiles and dashboard metrics'),
    ('10000000-0000-0000-0000-000000000037', N'quality.issues.view', N'View detailed data quality issues and problematic records'),
    ('10000000-0000-0000-0000-000000000038', N'quality.thresholds.manage', N'Manage dataset-level data quality thresholds');

INSERT INTO dbo.Permissions (Id, Name, Description)
SELECT Source.Id, Source.Name, Source.Description
FROM @DataQualityPermissions AS Source
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Permissions AS Existing
    WHERE Existing.Name = Source.Name
);

DECLARE @DataQualityRolePermissions TABLE
(
    RoleId uniqueidentifier,
    PermissionId uniqueidentifier
);

INSERT INTO @DataQualityRolePermissions (RoleId, PermissionId)
VALUES
    ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000035'),
    ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000036'),
    ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000037'),
    ('11111111-1111-1111-1111-111111111111', '10000000-0000-0000-0000-000000000038'),
    ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000035'),
    ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000036'),
    ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000037'),
    ('22222222-2222-2222-2222-222222222222', '10000000-0000-0000-0000-000000000038'),
    ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000035'),
    ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000036'),
    ('33333333-3333-3333-3333-333333333333', '10000000-0000-0000-0000-000000000037'),
    ('44444444-4444-4444-4444-444444444444', '10000000-0000-0000-0000-000000000036'),
    ('55555555-5555-5555-5555-555555555555', '10000000-0000-0000-0000-000000000036');

INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT Source.RoleId, Source.PermissionId
FROM @DataQualityRolePermissions AS Source
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.RolePermissions AS Existing
    WHERE Existing.RoleId = Source.RoleId
      AND Existing.PermissionId = Source.PermissionId
);
GO

------------------------------------------------------------
-- Analytics Dashboard APIs and Reporting: additive upgrade
------------------------------------------------------------
-- Additive, repeatable upgrade. Existing business rows are not modified.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DataImports') AND name=N'IX_Analytics_Imports_Dataset_Created')
    CREATE INDEX IX_Analytics_Imports_Dataset_Created
    ON dbo.DataImports(DatasetId, CreatedAtUtc DESC, Id DESC) INCLUDE(WorkspaceId, Status, CompletedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DataImports') AND name=N'IX_Analytics_Imports_Workspace_Completed')
    CREATE INDEX IX_Analytics_Imports_Workspace_Completed
    ON dbo.DataImports(WorkspaceId, CompletedAtUtc) INCLUDE(DatasetId, Status, StartedAtUtc, TotalRecords, SuccessfullyImportedRecords, RejectedRecords);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DataQualityProfileRuns') AND name=N'IX_Analytics_Profiles_Dataset_Completed')
    CREATE INDEX IX_Analytics_Profiles_Dataset_Completed
    ON dbo.DataQualityProfileRuns(DatasetId, CompletedAtUtc DESC, CreatedAtUtc DESC, Id DESC)
    INCLUDE(WorkspaceId, OverallQualityScore, ThresholdStatus) WHERE Status=N'Completed';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DataQualityProfileRuns') AND name=N'IX_Analytics_Profiles_Workspace_Completed')
    CREATE INDEX IX_Analytics_Profiles_Workspace_Completed
    ON dbo.DataQualityProfileRuns(WorkspaceId, CompletedAtUtc) INCLUDE(DatasetId, Status, OverallQualityScore);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DataQualityIssues') AND name=N'IX_Analytics_QualityIssues_Profile_Severity')
    CREATE INDEX IX_Analytics_QualityIssues_Profile_Severity
    ON dbo.DataQualityIssues(ProfileRunId, Severity) INCLUDE(DatasetId, WorkspaceId, IssueType);
GO
CREATE OR ALTER VIEW dbo.vw_DatasetAnalytics
AS
SELECT d.Id AS DatasetId, d.WorkspaceId, w.Name AS WorkspaceName,
       d.CategoryId, c.Name AS CategoryName, d.OwnerId, u.FullName AS OwnerName,
       d.Code, d.Name, d.Status, d.CreatedAtUtc, d.UpdatedAtUtc,
       records.CurrentRecordCount, p.Id AS LatestProfileRunId, p.CompletedAtUtc AS ProfileCompletedAtUtc,
       p.ScoringVersion, p.OverallQualityScore, p.CompletenessScore AS Completeness,
       p.ValidityScore AS Validity, p.UniquenessScore AS Uniqueness, p.ConsistencyScore AS Consistency,
       p.ThresholdStatus, p.AppliedOverallThreshold, p.AppliedCompletenessThreshold,
       p.AppliedValidityThreshold, p.AppliedUniquenessThreshold, p.AppliedConsistencyThreshold,
       latestImport.Id AS LatestImportId, latestImport.Status AS LatestImportStatus,
       committed.CompletedAtUtc AS LatestCommittedImportCompletedAtUtc,
       issues.QualityIssueCount, issues.ErrorIssueCount, invalid.InvalidValueCount,
       flags.NoCompletedProfile, flags.NoProfileAfterLatestImport, flags.FailedQualityThreshold,
       flags.LatestImportNeedsAttention, flags.SignificantQualityIssues,
       CAST(CASE WHEN flags.NoCompletedProfile=1 OR flags.NoProfileAfterLatestImport=1
           OR flags.FailedQualityThreshold=1 OR flags.LatestImportNeedsAttention=1
           OR flags.SignificantQualityIssues=1 THEN 1 ELSE 0 END AS bit) AS RequiresAttention
FROM dbo.Datasets d
JOIN dbo.Workspaces w ON w.Id=d.WorkspaceId
JOIN dbo.DatasetCategories c ON c.Id=d.CategoryId
JOIN dbo.AspNetUsers u ON u.Id=d.OwnerId
OUTER APPLY (
    SELECT TOP(1) q.* FROM dbo.DataQualityProfileRuns q
    WHERE q.DatasetId=d.Id AND q.WorkspaceId=d.WorkspaceId
      AND q.Status=N'Completed' AND q.CompletedAtUtc IS NOT NULL
    ORDER BY q.CompletedAtUtc DESC, q.CreatedAtUtc DESC, q.Id DESC
) p
OUTER APPLY (
    SELECT TOP(1) i.Id, i.Status FROM dbo.DataImports i
    WHERE i.DatasetId=d.Id AND i.WorkspaceId=d.WorkspaceId
    ORDER BY i.CreatedAtUtc DESC, i.Id DESC
) latestImport
OUTER APPLY (
    SELECT MAX(i.CompletedAtUtc) AS CompletedAtUtc FROM dbo.DataImports i
    WHERE i.DatasetId=d.Id AND i.WorkspaceId=d.WorkspaceId
      AND i.Status IN(N'Completed',N'Completed With Errors')
) committed
OUTER APPLY (
    SELECT COUNT_BIG(*) AS CurrentRecordCount FROM dbo.DatasetRecords r
    WHERE r.DatasetId=d.Id AND r.WorkspaceId=d.WorkspaceId
) records
OUTER APPLY (
    SELECT COUNT_BIG(*) AS QualityIssueCount,
           COALESCE(SUM(CASE WHEN i.Severity IN(N'Error',N'Critical') THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END),0) AS ErrorIssueCount
    FROM dbo.DataQualityIssues i
    WHERE i.ProfileRunId=p.Id AND i.DatasetId=d.Id AND i.WorkspaceId=d.WorkspaceId
) issues
OUTER APPLY (
    SELECT COALESCE(SUM(CAST(m.InvalidCount AS bigint)),0) AS InvalidValueCount
    FROM dbo.DataQualityColumnMetrics m WHERE m.ProfileRunId=p.Id AND m.WorkspaceId=d.WorkspaceId
) invalid
CROSS APPLY (
    SELECT CAST(CASE WHEN p.Id IS NULL THEN 1 ELSE 0 END AS bit) AS NoCompletedProfile,
           CAST(CASE WHEN p.Id IS NOT NULL AND committed.CompletedAtUtc > p.CompletedAtUtc THEN 1 ELSE 0 END AS bit) AS NoProfileAfterLatestImport,
           CAST(CASE WHEN p.ThresholdStatus=N'Failed' THEN 1 ELSE 0 END AS bit) AS FailedQualityThreshold,
           CAST(CASE WHEN latestImport.Status IN(N'Failed',N'Completed With Errors') THEN 1 ELSE 0 END AS bit) AS LatestImportNeedsAttention,
           CAST(CASE WHEN issues.ErrorIssueCount>0 OR invalid.InvalidValueCount>0 THEN 1 ELSE 0 END AS bit) AS SignificantQualityIssues
) flags
WHERE d.IsDeleted=0;
GO
CREATE OR ALTER VIEW dbo.vw_ImportAnalytics
AS
SELECT i.Id AS ImportId, i.DatasetId, i.WorkspaceId, d.Name AS DatasetName,
       i.Status, i.ImportMode, i.InitiatedByUserId,
       i.CreatedAtUtc, i.StartedAtUtc, i.CompletedAtUtc,
       CAST(i.TotalRecords AS bigint) AS RecordsAttempted,
       CAST(i.SuccessfullyImportedRecords AS bigint) AS RecordsSuccessfullyImported,
       CAST(i.RejectedRecords AS bigint) AS RecordsRejected,
       CAST(i.SuccessfullyImportedRecords AS bigint)+CAST(i.RejectedRecords AS bigint) AS RecordsProcessed,
       CASE WHEN CAST(i.TotalRecords AS bigint)>CAST(i.SuccessfullyImportedRecords AS bigint)+CAST(i.RejectedRecords AS bigint)
            THEN CAST(i.TotalRecords AS bigint)-CAST(i.SuccessfullyImportedRecords AS bigint)-CAST(i.RejectedRecords AS bigint)
            ELSE CAST(0 AS bigint) END AS RecordsWithoutFinalOutcome,
       CAST(CASE WHEN i.Status IN(N'Completed',N'Completed With Errors') THEN 1 ELSE 0 END AS bit) AS StatisticsComplete,
       CASE WHEN i.Status IN(N'Completed',N'Completed With Errors',N'Failed')
                 AND i.StartedAtUtc IS NOT NULL AND i.CompletedAtUtc>=i.StartedAtUtc
            THEN DATEDIFF_BIG(millisecond,i.StartedAtUtc,i.CompletedAtUtc) END AS ProcessingTimeMilliseconds
FROM dbo.DataImports i
JOIN dbo.Datasets d ON d.Id=i.DatasetId AND d.WorkspaceId=i.WorkspaceId
WHERE d.IsDeleted=0;
GO
CREATE OR ALTER VIEW dbo.vw_QualityTrendSource
AS
SELECT p.Id AS ProfileRunId, p.DatasetId, p.WorkspaceId, p.CompletedAtUtc, p.CreatedAtUtc AS ProfileCreatedAtUtc,
       CAST(CAST(p.CompletedAtUtc AS date) AS datetime2) AS DayUtc,
       CAST(DATEADD(day,-((DATEDIFF(day,CONVERT(date,'19000101',112),p.CompletedAtUtc)%7+7)%7),
           CAST(p.CompletedAtUtc AS date)) AS datetime2) AS WeekUtc,
       CAST(DATEFROMPARTS(YEAR(p.CompletedAtUtc),MONTH(p.CompletedAtUtc),1) AS datetime2) AS MonthUtc,
       p.OverallQualityScore, p.CompletenessScore AS Completeness, p.ValidityScore AS Validity,
       p.UniquenessScore AS Uniqueness, p.ConsistencyScore AS Consistency
FROM dbo.DataQualityProfileRuns p
JOIN dbo.Datasets d ON d.Id=p.DatasetId AND d.WorkspaceId=p.WorkspaceId
WHERE p.Status=N'Completed' AND p.CompletedAtUtc IS NOT NULL AND d.IsDeleted=0;
GO
IF NOT EXISTS(SELECT 1 FROM dbo.Permissions WHERE Name=N'reports.export')
    INSERT INTO dbo.Permissions(Id,Name,Description)
    VALUES('10000000-0000-0000-0000-000000000039',N'reports.export',N'Export permitted analytics reports');
INSERT INTO dbo.RolePermissions(RoleId,PermissionId)
SELECT r.Id,p.Id FROM dbo.AspNetRoles r CROSS JOIN dbo.Permissions p
WHERE r.Name IN(N'Platform Administrator',N'Workspace Administrator',N'Data Analyst')
  AND p.Name=N'reports.export'
  AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
GO

------------------------------------------------------------
-- Data Lineage and Impact Analysis additive upgrade
------------------------------------------------------------
-- New activities are captured after deployment; no historical backfill is performed.
IF OBJECT_ID(N'dbo.LineageRelationships', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LineageRelationships
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_LineageRelationships PRIMARY KEY,
        WorkspaceId uniqueidentifier NOT NULL,
        DatasetId uniqueidentifier NOT NULL,
        OwnerId uniqueidentifier NOT NULL,
        SourceEntityType nvarchar(40) NOT NULL,
        SourceEntityId uniqueidentifier NOT NULL,
        SourceDatasetId uniqueidentifier NULL,
        SourceDisplayName nvarchar(500) NOT NULL,
        SourceVersionNumber int NULL,
        TargetEntityType nvarchar(40) NOT NULL,
        TargetEntityId uniqueidentifier NOT NULL,
        TargetDatasetId uniqueidentifier NULL,
        TargetDisplayName nvarchar(500) NOT NULL,
        TargetVersionNumber int NULL,
        RelationshipType nvarchar(60) NOT NULL,
        RelevantDatasetVersionId uniqueidentifier NULL,
        RelevantDatasetVersionNumber int NULL,
        ProcessEntityType nvarchar(40) NULL,
        ProcessEntityId uniqueidentifier NULL,
        MetadataJson nvarchar(4000) NOT NULL,
        IsAutomatic bit NOT NULL,
        IsActive bit NOT NULL,
        CreatedByUserId uniqueidentifier NULL,
        CreatedAtUtc datetime2 NOT NULL,
        UpdatedByUserId uniqueidentifier NULL,
        UpdatedAtUtc datetime2 NULL,
        DeactivatedByUserId uniqueidentifier NULL,
        DeactivatedAtUtc datetime2 NULL,
        DeactivationReason nvarchar(1000) NULL,
        CONSTRAINT FK_LineageRelationships_Datasets_DatasetId
            FOREIGN KEY (DatasetId) REFERENCES dbo.Datasets(Id),
        CONSTRAINT FK_LineageRelationships_DatasetVersions_RelevantDatasetVersionId
            FOREIGN KEY (RelevantDatasetVersionId) REFERENCES dbo.DatasetVersions(Id)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.LineageRelationships') AND name=N'IX_LineageRelationships_Source_Target_Type_Active')
    CREATE UNIQUE INDEX IX_LineageRelationships_Source_Target_Type_Active
    ON dbo.LineageRelationships(WorkspaceId,SourceEntityType,SourceEntityId,TargetEntityType,TargetEntityId,RelationshipType)
    WHERE IsActive=1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.LineageRelationships') AND name=N'IX_LineageRelationships_Workspace_Dataset_Active_Created')
    CREATE INDEX IX_LineageRelationships_Workspace_Dataset_Active_Created
    ON dbo.LineageRelationships(WorkspaceId,DatasetId,IsActive,CreatedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.LineageRelationships') AND name=N'IX_LineageRelationships_Workspace_Source_Active')
    CREATE INDEX IX_LineageRelationships_Workspace_Source_Active
    ON dbo.LineageRelationships(WorkspaceId,SourceEntityType,SourceEntityId,IsActive);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.LineageRelationships') AND name=N'IX_LineageRelationships_Workspace_Target_Active')
    CREATE INDEX IX_LineageRelationships_Workspace_Target_Active
    ON dbo.LineageRelationships(WorkspaceId,TargetEntityType,TargetEntityId,IsActive);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.LineageRelationships') AND name=N'IX_LineageRelationships_RelevantDatasetVersionId')
    CREATE INDEX IX_LineageRelationships_RelevantDatasetVersionId ON dbo.LineageRelationships(RelevantDatasetVersionId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.LineageRelationships') AND name=N'IX_LineageRelationships_ProcessEntityId')
    CREATE INDEX IX_LineageRelationships_ProcessEntityId ON dbo.LineageRelationships(ProcessEntityId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.LineageRelationships') AND name=N'IX_LineageRelationships_DatasetId')
    CREATE INDEX IX_LineageRelationships_DatasetId ON dbo.LineageRelationships(DatasetId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.LineageRelationships') AND name=N'IX_LineageRelationships_Workspace_Owner_Created')
    CREATE INDEX IX_LineageRelationships_Workspace_Owner_Created ON dbo.LineageRelationships(WorkspaceId,OwnerId,CreatedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.LineageRelationships') AND name=N'IX_LineageRelationships_Workspace_CreatedBy_Created')
    CREATE INDEX IX_LineageRelationships_Workspace_CreatedBy_Created ON dbo.LineageRelationships(WorkspaceId,CreatedByUserId,CreatedAtUtc);
GO

IF NOT EXISTS(SELECT 1 FROM dbo.Permissions WHERE Name=N'lineage.view')
    INSERT dbo.Permissions(Id,Name,Description) VALUES('10000000-0000-0000-0000-000000000040',N'lineage.view',N'View permitted lineage graphs and relationships');
IF NOT EXISTS(SELECT 1 FROM dbo.Permissions WHERE Name=N'lineage.manage')
    INSERT dbo.Permissions(Id,Name,Description) VALUES('10000000-0000-0000-0000-000000000041',N'lineage.manage',N'Manage lineage relationships within the permitted workspace');
IF NOT EXISTS(SELECT 1 FROM dbo.Permissions WHERE Name=N'lineage.impact')
    INSERT dbo.Permissions(Id,Name,Description) VALUES('10000000-0000-0000-0000-000000000042',N'lineage.impact',N'Run downstream lineage impact analysis');

DECLARE @LineageRolePermissions TABLE(RoleName nvarchar(256), PermissionName nvarchar(150));
INSERT @LineageRolePermissions VALUES
    (N'Platform Administrator',N'lineage.view'),(N'Platform Administrator',N'lineage.manage'),(N'Platform Administrator',N'lineage.impact'),
    (N'Workspace Administrator',N'lineage.view'),(N'Workspace Administrator',N'lineage.manage'),(N'Workspace Administrator',N'lineage.impact'),
    (N'Data Analyst',N'lineage.view'),(N'Data Analyst',N'lineage.impact'),
    (N'Business User',N'lineage.view'),(N'Viewer',N'lineage.view');
INSERT dbo.RolePermissions(RoleId,PermissionId)
SELECT r.Id,p.Id FROM @LineageRolePermissions m
JOIN dbo.AspNetRoles r ON r.Name=m.RoleName
JOIN dbo.Permissions p ON p.Name=m.PermissionName
WHERE NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
GO

-- SSMS verification (lineage uses a table, not a SQL view):
SELECT OBJECT_ID(N'dbo.LineageRelationships', N'U') AS LineageTableObjectId;
SELECT name FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.LineageRelationships') ORDER BY name;
SELECT Name FROM dbo.Permissions WHERE Name LIKE N'lineage.%' ORDER BY Name;
GO

-- Data governance, classification, certification and dataset-level access policy
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915090000_Task22DataGovernanceAccessPolicy'
)
BEGIN
    CREATE TABLE dbo.DatasetGovernance(
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DatasetGovernance PRIMARY KEY,
        WorkspaceId uniqueidentifier NOT NULL,
        DatasetId uniqueidentifier NOT NULL,
        DataStewardId uniqueidentifier NULL,
        BusinessDescription nvarchar(4000) NOT NULL CONSTRAINT DF_DatasetGovernance_BusinessDescription DEFAULT N'',
        GovernanceStatus nvarchar(30) NOT NULL,
        Classification nvarchar(30) NOT NULL,
        LastReviewedDateUtc datetime2 NULL,
        NextReviewDateUtc datetime2 NULL,
        UpdatedByUserId uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2 NOT NULL,
        UpdatedAtUtc datetime2 NOT NULL,
        CONSTRAINT FK_DatasetGovernance_Datasets_DatasetId FOREIGN KEY(DatasetId) REFERENCES dbo.Datasets(Id) ON DELETE CASCADE,
        CONSTRAINT FK_DatasetGovernance_AspNetUsers_DataStewardId FOREIGN KEY(DataStewardId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT CK_DatasetGovernance_Classification CHECK (Classification IN (N'Public',N'Internal',N'Confidential',N'Restricted')),
        CONSTRAINT CK_DatasetGovernance_Status CHECK (GovernanceStatus IN (N'Draft',N'Under Review',N'Certified',N'Expired',N'Archived'))
    );
    CREATE UNIQUE INDEX IX_DatasetGovernance_DatasetId ON dbo.DatasetGovernance(DatasetId);
    CREATE INDEX IX_DatasetGovernance_WorkspaceId_Classification_GovernanceStatus ON dbo.DatasetGovernance(WorkspaceId,Classification,GovernanceStatus);
    CREATE INDEX IX_DatasetGovernance_DataStewardId ON dbo.DatasetGovernance(DataStewardId);

    CREATE TABLE dbo.DatasetCertifications(
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DatasetCertifications PRIMARY KEY,
        WorkspaceId uniqueidentifier NOT NULL,
        DatasetId uniqueidentifier NOT NULL,
        SubmittedByUserId uniqueidentifier NOT NULL,
        SubmittedAtUtc datetime2 NOT NULL,
        CertifiedByUserId uniqueidentifier NULL,
        CertifiedDateUtc datetime2 NULL,
        CertificationStatus nvarchar(30) NOT NULL,
        ReviewExpiryDateUtc datetime2 NULL,
        Comments nvarchar(2000) NULL,
        ReviewedAtUtc datetime2 NULL,
        CONSTRAINT FK_DatasetCertifications_Datasets_DatasetId FOREIGN KEY(DatasetId) REFERENCES dbo.Datasets(Id) ON DELETE CASCADE,
        CONSTRAINT FK_DatasetCertifications_AspNetUsers_SubmittedByUserId FOREIGN KEY(SubmittedByUserId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_DatasetCertifications_AspNetUsers_CertifiedByUserId FOREIGN KEY(CertifiedByUserId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT CK_DatasetCertifications_Status CHECK (CertificationStatus IN (N'Pending',N'Certified',N'Rejected',N'Expired'))
    );
    CREATE INDEX IX_DatasetCertifications_WorkspaceId_DatasetId_SubmittedAtUtc ON dbo.DatasetCertifications(WorkspaceId,DatasetId,SubmittedAtUtc);
    CREATE UNIQUE INDEX IX_DatasetCertifications_DatasetId_Pending ON dbo.DatasetCertifications(DatasetId) WHERE CertificationStatus=N'Pending';
    CREATE INDEX IX_DatasetCertifications_SubmittedByUserId ON dbo.DatasetCertifications(SubmittedByUserId);
    CREATE INDEX IX_DatasetCertifications_CertifiedByUserId ON dbo.DatasetCertifications(CertifiedByUserId);

    CREATE TABLE dbo.DatasetAccessRequests(
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DatasetAccessRequests PRIMARY KEY,
        WorkspaceId uniqueidentifier NOT NULL,
        DatasetId uniqueidentifier NOT NULL,
        RequestingUserId uniqueidentifier NOT NULL,
        RequestedAccessLevel nvarchar(20) NOT NULL,
        BusinessJustification nvarchar(2000) NOT NULL,
        RequestDateUtc datetime2 NOT NULL,
        Status nvarchar(20) NOT NULL,
        ReviewerId uniqueidentifier NULL,
        ReviewComments nvarchar(2000) NULL,
        ReviewedDateUtc datetime2 NULL,
        RequestedExpiryDateUtc datetime2 NULL,
        CONSTRAINT FK_DatasetAccessRequests_Datasets_DatasetId FOREIGN KEY(DatasetId) REFERENCES dbo.Datasets(Id) ON DELETE CASCADE,
        CONSTRAINT FK_DatasetAccessRequests_AspNetUsers_RequestingUserId FOREIGN KEY(RequestingUserId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_DatasetAccessRequests_AspNetUsers_ReviewerId FOREIGN KEY(ReviewerId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT CK_DatasetAccessRequests_Level CHECK (RequestedAccessLevel IN (N'Read',N'Write',N'Manage')),
        CONSTRAINT CK_DatasetAccessRequests_Status CHECK (Status IN (N'Pending',N'Approved',N'Rejected',N'Revoked'))
    );
    CREATE INDEX IX_DatasetAccessRequests_WorkspaceId_DatasetId_RequestDateUtc ON dbo.DatasetAccessRequests(WorkspaceId,DatasetId,RequestDateUtc);
    CREATE UNIQUE INDEX IX_DatasetAccessRequests_Dataset_User_Level_Pending ON dbo.DatasetAccessRequests(DatasetId,RequestingUserId,RequestedAccessLevel) WHERE Status=N'Pending';
    CREATE INDEX IX_DatasetAccessRequests_RequestingUserId ON dbo.DatasetAccessRequests(RequestingUserId);
    CREATE INDEX IX_DatasetAccessRequests_ReviewerId ON dbo.DatasetAccessRequests(ReviewerId);

    CREATE TABLE dbo.DatasetAccessGrants(
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DatasetAccessGrants PRIMARY KEY,
        WorkspaceId uniqueidentifier NOT NULL,
        DatasetId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        AccessLevel nvarchar(20) NOT NULL,
        GrantedByUserId uniqueidentifier NOT NULL,
        GrantedDateUtc datetime2 NOT NULL,
        ExpiryDateUtc datetime2 NULL,
        Status nvarchar(20) NOT NULL,
        RevokedByUserId uniqueidentifier NULL,
        RevokedDateUtc datetime2 NULL,
        SourceAccessRequestId uniqueidentifier NOT NULL,
        CONSTRAINT FK_DatasetAccessGrants_Datasets_DatasetId FOREIGN KEY(DatasetId) REFERENCES dbo.Datasets(Id) ON DELETE CASCADE,
        CONSTRAINT FK_DatasetAccessGrants_AspNetUsers_UserId FOREIGN KEY(UserId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_DatasetAccessGrants_AspNetUsers_GrantedByUserId FOREIGN KEY(GrantedByUserId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_DatasetAccessGrants_AspNetUsers_RevokedByUserId FOREIGN KEY(RevokedByUserId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_DatasetAccessGrants_DatasetAccessRequests_SourceAccessRequestId FOREIGN KEY(SourceAccessRequestId) REFERENCES dbo.DatasetAccessRequests(Id),
        CONSTRAINT CK_DatasetAccessGrants_Level CHECK (AccessLevel IN (N'Read',N'Write',N'Manage')),
        CONSTRAINT CK_DatasetAccessGrants_Status CHECK (Status IN (N'Active',N'Revoked',N'Expired'))
    );
    CREATE INDEX IX_DatasetAccessGrants_WorkspaceId_DatasetId_UserId ON dbo.DatasetAccessGrants(WorkspaceId,DatasetId,UserId);
    CREATE UNIQUE INDEX IX_DatasetAccessGrants_Dataset_User_Active ON dbo.DatasetAccessGrants(DatasetId,UserId) WHERE Status=N'Active';
    CREATE INDEX IX_DatasetAccessGrants_UserId ON dbo.DatasetAccessGrants(UserId);
    CREATE INDEX IX_DatasetAccessGrants_GrantedByUserId ON dbo.DatasetAccessGrants(GrantedByUserId);
    CREATE INDEX IX_DatasetAccessGrants_RevokedByUserId ON dbo.DatasetAccessGrants(RevokedByUserId);
    CREATE INDEX IX_DatasetAccessGrants_SourceAccessRequestId ON dbo.DatasetAccessGrants(SourceAccessRequestId);

    CREATE TABLE dbo.DatasetAccessHistory(
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DatasetAccessHistory PRIMARY KEY,
        WorkspaceId uniqueidentifier NOT NULL,
        DatasetId uniqueidentifier NOT NULL,
        AccessRequestId uniqueidentifier NOT NULL,
        Action nvarchar(30) NOT NULL,
        FromStatus nvarchar(20) NOT NULL,
        ToStatus nvarchar(20) NOT NULL,
        PerformedByUserId uniqueidentifier NOT NULL,
        Comments nvarchar(2000) NULL,
        PerformedAtUtc datetime2 NOT NULL,
        CONSTRAINT FK_DatasetAccessHistory_DatasetAccessRequests_AccessRequestId FOREIGN KEY(AccessRequestId) REFERENCES dbo.DatasetAccessRequests(Id) ON DELETE CASCADE
        ,CONSTRAINT FK_DatasetAccessHistory_AspNetUsers_PerformedByUserId FOREIGN KEY(PerformedByUserId) REFERENCES dbo.AspNetUsers(Id)
    );
    CREATE INDEX IX_DatasetAccessHistory_WorkspaceId_AccessRequestId_PerformedAtUtc ON dbo.DatasetAccessHistory(WorkspaceId,AccessRequestId,PerformedAtUtc);
    CREATE INDEX IX_DatasetAccessHistory_PerformedByUserId ON dbo.DatasetAccessHistory(PerformedByUserId);

    INSERT dbo.DatasetGovernance(Id,WorkspaceId,DatasetId,DataStewardId,BusinessDescription,GovernanceStatus,Classification,
        LastReviewedDateUtc,NextReviewDateUtc,UpdatedByUserId,CreatedAtUtc,UpdatedAtUtc)
    SELECT NEWID(),d.WorkspaceId,d.Id,NULL,COALESCE(d.Description,N''),N'Draft',N'Internal',NULL,NULL,d.OwnerId,SYSUTCDATETIME(),SYSUTCDATETIME()
    FROM dbo.Datasets d WHERE NOT EXISTS(SELECT 1 FROM dbo.DatasetGovernance g WHERE g.DatasetId=d.Id);

    DECLARE @Permissions TABLE(Id uniqueidentifier,Name nvarchar(150),Description nvarchar(500));
    INSERT @Permissions VALUES
    ('10000000-0000-0000-0000-000000000043',N'governance.view',N'View dataset governance metadata and history'),
    ('10000000-0000-0000-0000-000000000044',N'governance.manage',N'Manage dataset governance metadata'),
    ('10000000-0000-0000-0000-000000000045',N'governance.certify',N'Review and certify governed datasets'),
    ('10000000-0000-0000-0000-000000000046',N'datasetaccess.request',N'Request dataset access and view permitted requests'),
    ('10000000-0000-0000-0000-000000000047',N'datasetaccess.review',N'Approve or reject dataset access requests'),
    ('10000000-0000-0000-0000-000000000048',N'datasetaccess.revoke',N'Revoke dataset access grants'),
    ('10000000-0000-0000-0000-000000000049',N'governance.dashboard',N'View governance dashboard data');
    INSERT dbo.Permissions(Id,Name,Description)
    SELECT p.Id,p.Name,p.Description FROM @Permissions p WHERE NOT EXISTS(SELECT 1 FROM dbo.Permissions x WHERE x.Name=p.Name);

    DECLARE @Mappings TABLE(RoleName nvarchar(256),PermissionName nvarchar(150));
    INSERT @Mappings VALUES
    (N'Platform Administrator',N'governance.view'),(N'Platform Administrator',N'governance.manage'),(N'Platform Administrator',N'governance.certify'),(N'Platform Administrator',N'datasetaccess.request'),(N'Platform Administrator',N'datasetaccess.review'),(N'Platform Administrator',N'datasetaccess.revoke'),(N'Platform Administrator',N'governance.dashboard'),
    (N'Workspace Administrator',N'governance.view'),(N'Workspace Administrator',N'governance.manage'),(N'Workspace Administrator',N'governance.certify'),(N'Workspace Administrator',N'datasetaccess.request'),(N'Workspace Administrator',N'datasetaccess.review'),(N'Workspace Administrator',N'datasetaccess.revoke'),(N'Workspace Administrator',N'governance.dashboard'),
    (N'Data Analyst',N'governance.view'),(N'Data Analyst',N'governance.manage'),(N'Data Analyst',N'governance.certify'),(N'Data Analyst',N'datasetaccess.request'),(N'Data Analyst',N'datasetaccess.review'),(N'Data Analyst',N'datasetaccess.revoke'),(N'Data Analyst',N'governance.dashboard'),
    (N'Business User',N'governance.view'),(N'Business User',N'governance.manage'),(N'Business User',N'governance.certify'),(N'Business User',N'datasetaccess.request'),(N'Business User',N'datasetaccess.review'),(N'Business User',N'datasetaccess.revoke'),
    (N'Viewer',N'governance.view'),(N'Viewer',N'governance.manage'),(N'Viewer',N'governance.certify'),(N'Viewer',N'datasetaccess.request'),(N'Viewer',N'datasetaccess.review'),(N'Viewer',N'datasetaccess.revoke');
    INSERT dbo.RolePermissions(RoleId,PermissionId)
    SELECT r.Id,p.Id FROM @Mappings m JOIN dbo.AspNetRoles r ON r.Name=m.RoleName JOIN dbo.Permissions p ON p.Name=m.PermissionName
    WHERE NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260915090000_Task22DataGovernanceAccessPolicy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260915090000_Task22DataGovernanceAccessPolicy', N'8.0.22');
END;
GO

COMMIT;
GO

-- Final governance verification: expect five Present rows and one migration row.
SELECT DB_NAME() AS CurrentDatabase;

SELECT required.TableName,
       CASE WHEN OBJECT_ID(N'dbo.' + required.TableName, N'U') IS NULL
            THEN N'Missing' ELSE N'Present' END AS TableStatus
FROM (VALUES
    (N'DatasetGovernance'),
    (N'DatasetCertifications'),
    (N'DatasetAccessRequests'),
    (N'DatasetAccessGrants'),
    (N'DatasetAccessHistory')
) AS required(TableName)
ORDER BY required.TableName;

SELECT MigrationId, ProductVersion
FROM dbo.__EFMigrationsHistory
WHERE MigrationId = N'20260915090000_Task22DataGovernanceAccessPolicy';

-- Expect seven governance/access permissions.
SELECT Id, Name
FROM dbo.Permissions
WHERE Name LIKE N'governance.%' OR Name LIKE N'datasetaccess.%'
ORDER BY Name;
GO

/* ============================================================
   Data Sharing, Export & Secure Delivery
   Migration: 20260924090000_Task23DataSharingExportSecureDelivery
   ============================================================ */
IF OBJECT_ID(N'dbo.DataExportRequests', N'U') IS NULL
BEGIN
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
        CONSTRAINT FK_DataExportRequests_Datasets_DatasetId FOREIGN KEY(DatasetId) REFERENCES dbo.Datasets(Id),
        CONSTRAINT FK_DataExportRequests_DatasetVersions_DatasetVersionId FOREIGN KEY(DatasetVersionId) REFERENCES dbo.DatasetVersions(Id),
        CONSTRAINT FK_DataExportRequests_AspNetUsers_RequestedByUserId FOREIGN KEY(RequestedByUserId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT CK_DataExportRequests_Format CHECK (Format IN (N'CSV',N'Excel')),
        CONSTRAINT CK_DataExportRequests_Status CHECK (Status IN (N'Pending',N'Processing',N'Completed',N'Failed',N'Cancelled'))
    );
    CREATE INDEX IX_DataExportRequests_WorkspaceId_DatasetId_RequestedAtUtc ON dbo.DataExportRequests(WorkspaceId,DatasetId,RequestedAtUtc);
    CREATE INDEX IX_DataExportRequests_WorkspaceId_Status_RequestedAtUtc ON dbo.DataExportRequests(WorkspaceId,Status,RequestedAtUtc);
    CREATE INDEX IX_DataExportRequests_DatasetId ON dbo.DataExportRequests(DatasetId);
    CREATE INDEX IX_DataExportRequests_DatasetVersionId ON dbo.DataExportRequests(DatasetVersionId);
    CREATE INDEX IX_DataExportRequests_RequestedByUserId ON dbo.DataExportRequests(RequestedByUserId);
END;
GO

IF OBJECT_ID(N'dbo.DatasetShareHistories', N'U') IS NULL
BEGIN
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
        CONSTRAINT FK_DatasetShareHistories_Datasets_DatasetId FOREIGN KEY(DatasetId) REFERENCES dbo.Datasets(Id),
        CONSTRAINT FK_DatasetShareHistories_DatasetAccessGrants_DatasetAccessGrantId FOREIGN KEY(DatasetAccessGrantId) REFERENCES dbo.DatasetAccessGrants(Id),
        CONSTRAINT FK_DatasetShareHistories_AspNetUsers_RecipientUserId FOREIGN KEY(RecipientUserId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_DatasetShareHistories_AspNetUsers_SharedByUserId FOREIGN KEY(SharedByUserId) REFERENCES dbo.AspNetUsers(Id),
        CONSTRAINT FK_DatasetShareHistories_AspNetUsers_RevokedByUserId FOREIGN KEY(RevokedByUserId) REFERENCES dbo.AspNetUsers(Id),
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
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260924090000_Task23DataSharingExportSecureDelivery')
BEGIN
    INSERT INTO dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (N'20260924090000_Task23DataSharingExportSecureDelivery', N'8.0.22');
END;
GO

-- Task 23 verification
SELECT required.TableName,
       CASE WHEN OBJECT_ID(N'dbo.' + required.TableName, N'U') IS NULL THEN N'Missing' ELSE N'Present' END AS TableStatus
FROM (VALUES (N'DataExportRequests'), (N'DatasetShareHistories'), (N'DatasetAccessGrants')) required(TableName)
ORDER BY required.TableName;

SELECT MigrationId, ProductVersion
FROM dbo.__EFMigrationsHistory
WHERE MigrationId = N'20260924090000_Task23DataSharingExportSecureDelivery';
GO
