using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Tests.Infrastructure;

internal static class EntityFactory
{
    internal static ApiKeyEntity ApiKey(string name = "Test Key") => new()
    {
        Id = Guid.CreateVersion7(),
        Prefix = "TESTKEY1",
        KeyHash = "testhash",
        Name = name,
        Scopes = "links:write",
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
    };

    internal static LinkEntity AnonymousLink(Guid apiKeyId) => new()
    {
        Id = Guid.CreateVersion7(),
        OriginalUrl = "https://example.com",
        ApiKeyId = apiKeyId,
        Mode = LinkMode.Anonymous,
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
    };

    // A dashboard-created link: owned directly by a user, no API key.
    internal static LinkEntity UserOwnedLink(Guid userAccountId) => new()
    {
        Id = Guid.CreateVersion7(),
        OriginalUrl = "https://example.com",
        UserAccountId = userAccountId,
        Mode = LinkMode.Anonymous,
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
    };

    internal static ShortCodeEntity ShortCode(Guid linkId, string code) => new()
    {
        Id = Guid.CreateVersion7(),
        LinkId = linkId,
        Code = code,
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
    };

    internal static UserLinkCodeEntity UserLinkCode(Guid linkId, Guid userId, string code) => new()
    {
        Id = Guid.CreateVersion7(),
        LinkId = linkId,
        UserId = userId,
        Code = code,
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
        IsOneTimeUse = false,
        IsUsed = false,
    };

    internal static VisitEntity Visit(Guid shortCodeId) => new()
    {
        Id = Guid.CreateVersion7(),
        ShortCodeId = shortCodeId,
        ClickedAt = DateTimeOffset.UtcNow,
        HashedIp = "hashed-ip",
        Referrer = null,
        UserAgent = null,
    };

    internal static UserVisitEntity UserVisit(Guid userLinkCodeId, Guid userId) => new()
    {
        Id = Guid.CreateVersion7(),
        UserLinkCodeId = userLinkCodeId,
        UserId = userId,
        ClickedAt = DateTimeOffset.UtcNow,
        HashedIp = "hashed-ip",
        Referrer = null,
        UserAgent = null,
    };

    internal static UserAccountEntity UserAccount(string email = "user@example.com") => new()
    {
        Id = Guid.CreateVersion7(),
        Email = email,
        DisplayName = null,
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
    };

    internal static MagicLinkTokenEntity MagicLinkToken(Guid userAccountId) => new()
    {
        Id = Guid.CreateVersion7(),
        UserAccountId = userAccountId,
        TokenHash = "sha256hashvalue",
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
        UsedAt = null,
    };

    internal static CustomDomainEntity CustomDomain(Guid userAccountId, string domain = "go.example.com") => new()
    {
        Id = Guid.CreateVersion7(),
        UserAccountId = userAccountId,
        Domain = domain,
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = false,
        VerificationStatus = DomainVerificationStatus.Pending,
        VerificationToken = "verify-me-txt-value",
        VerifiedAt = null,
    };

    internal static AccountEntity Account(string name = "Test Account") => new()
    {
        Id = Guid.CreateVersion7(),
        Name = name,
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true,
    };

    internal static MembershipEntity Membership(Guid accountId, Guid userAccountId, AccountRole role = AccountRole.Owner) => new()
    {
        Id = Guid.CreateVersion7(),
        AccountId = accountId,
        UserAccountId = userAccountId,
        Role = role,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
