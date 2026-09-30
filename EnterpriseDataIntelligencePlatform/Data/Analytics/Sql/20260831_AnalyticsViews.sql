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
