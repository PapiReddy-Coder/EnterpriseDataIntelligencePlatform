using System.ComponentModel.DataAnnotations;

namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed record UpsertDatasetGovernanceRequest(
    Guid? DataStewardId,
    [Required, MaxLength(4000)] string BusinessDescription,
    [Required, MaxLength(30)] string GovernanceStatus,
    [Required, MaxLength(30)] string Classification,
    DateTime? LastReviewedDateUtc,
    DateTime? NextReviewDateUtc);

public sealed record UpdateClassificationRequest([Required, MaxLength(30)] string Classification);
public sealed record AssignGovernanceUsersRequest(Guid DataOwnerId, Guid? DataStewardId);
public sealed record SubmitCertificationRequest([MaxLength(2000)] string? Comments);
public sealed record ReviewCertificationRequest(bool Approve, DateTime? ReviewExpiryDateUtc, [MaxLength(2000)] string? Comments);
public sealed record CreateDatasetAccessRequest(
    [Required, MaxLength(20)] string RequestedAccessLevel,
    [Required, MinLength(10), MaxLength(2000)] string BusinessJustification,
    DateTime? RequestedExpiryDateUtc);
public sealed record ReviewAccessRequest(bool Approve, DateTime? ExpiryDateUtc, [MaxLength(2000)] string? Comments);
public sealed record RevokeDatasetAccessRequest([Required, MaxLength(1000)] string Reason);

public sealed record DatasetGovernanceResponse(
    Guid DatasetId, string DatasetCode, string DatasetName, Guid WorkspaceId,
    Guid DataOwnerId, string DataOwner, Guid? DataStewardId, string? DataSteward,
    string BusinessDescription, string GovernanceStatus, string Classification,
    DateTime? LastReviewedDateUtc, DateTime? NextReviewDateUtc,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record DatasetCertificationResponse(
    Guid Id, Guid DatasetId, Guid SubmittedByUserId, DateTime SubmittedAtUtc,
    Guid? CertifiedByUserId, DateTime? CertifiedDateUtc, string CertificationStatus,
    DateTime? ReviewExpiryDateUtc, string? Comments, DateTime? ReviewedAtUtc);

public sealed record DatasetAccessRequestResponse(
    Guid Id, Guid DatasetId, string DatasetName, Guid RequestingUserId, string RequestingUser,
    string RequestedAccessLevel, string BusinessJustification, DateTime RequestDateUtc,
    string Status, Guid? ReviewerId, string? Reviewer, string? ReviewComments,
    DateTime? ReviewedDateUtc, DateTime? RequestedExpiryDateUtc);

public sealed record DatasetAccessGrantResponse(
    Guid Id, Guid DatasetId, Guid UserId, string AccessLevel, Guid GrantedByUserId,
    DateTime GrantedDateUtc, DateTime? ExpiryDateUtc, string Status,
    Guid? RevokedByUserId, DateTime? RevokedDateUtc, Guid SourceAccessRequestId);

public sealed record DatasetAccessHistoryResponse(
    Guid Id, Guid AccessRequestId, string Action, string FromStatus, string ToStatus,
    Guid PerformedByUserId, string? Comments, DateTime PerformedAtUtc);

public sealed record GovernanceSearchRequest(
    string? Keyword = null, string? Classification = null, string? GovernanceStatus = null,
    Guid? OwnerId = null, Guid? StewardId = null, bool? ReviewOverdue = null,
    int Page = 1, int PageSize = 20);

public sealed record AccessRequestSearchRequest(
    Guid? DatasetId = null, Guid? RequestingUserId = null, string? Status = null,
    int Page = 1, int PageSize = 20);

public sealed record ClassificationCount(string Classification, int Count);
public sealed record RecentAccessDecision(Guid RequestId, Guid DatasetId, string DatasetName,
    Guid RequestingUserId, string Status, DateTime DecisionDateUtc);
public sealed record GovernanceDashboardResponse(
    int TotalDatasets, int CertifiedDatasets, int DatasetsPendingReview,
    int DatasetsWithExpiredCertification, IReadOnlyList<ClassificationCount> DatasetsByClassification,
    int PendingAccessRequests, IReadOnlyList<RecentAccessDecision> RecentAccessDecisions);
