namespace EnterpriseDataIntelligencePlatform.Domain;

public static class DataClassifications
{
    public const string Public = "Public";
    public const string Internal = "Internal";
    public const string Confidential = "Confidential";
    public const string Restricted = "Restricted";
    public static readonly string[] All = [Public, Internal, Confidential, Restricted];
    public static bool RequiresGrant(string value) =>
        value is Confidential or Restricted;
}

public static class GovernanceStatuses
{
    public const string Draft = "Draft";
    public const string UnderReview = "Under Review";
    public const string Certified = "Certified";
    public const string Expired = "Expired";
    public const string Archived = "Archived";
    public static readonly string[] All = [Draft, UnderReview, Certified, Expired, Archived];
}

public static class CertificationStatuses
{
    public const string Pending = "Pending";
    public const string Certified = "Certified";
    public const string Rejected = "Rejected";
    public const string Expired = "Expired";
    public static readonly string[] All = [Pending, Certified, Rejected, Expired];
}

public static class DatasetAccessLevels
{
    public const string Read = "Read";
    public const string Write = "Write";
    public const string Manage = "Manage";
    public static readonly string[] All = [Read, Write, Manage];

    public static int Rank(string value) => value switch
    {
        Read => 1,
        Write => 2,
        Manage => 3,
        _ => 0
    };
}

public static class AccessRequestStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Revoked = "Revoked";
    public static readonly string[] All = [Pending, Approved, Rejected, Revoked];
}

public static class AccessGrantStatuses
{
    public const string Active = "Active";
    public const string Revoked = "Revoked";
    public const string Expired = "Expired";
}
