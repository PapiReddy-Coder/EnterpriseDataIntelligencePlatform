using EnterpriseDataIntelligencePlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseDataIntelligencePlatform.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260915090000_Task22DataGovernanceAccessPolicy")]
public sealed class Task22DataGovernanceAccessPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
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
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DELETE rp FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id=rp.PermissionId
WHERE p.Name IN (N'governance.view',N'governance.manage',N'governance.certify',N'datasetaccess.request',N'datasetaccess.review',N'datasetaccess.revoke',N'governance.dashboard');
DELETE dbo.Permissions WHERE Name IN (N'governance.view',N'governance.manage',N'governance.certify',N'datasetaccess.request',N'datasetaccess.review',N'datasetaccess.revoke',N'governance.dashboard');
DROP TABLE dbo.DatasetAccessHistory;
DROP TABLE dbo.DatasetAccessGrants;
DROP TABLE dbo.DatasetAccessRequests;
DROP TABLE dbo.DatasetCertifications;
DROP TABLE dbo.DatasetGovernance;
""");
    }
}
