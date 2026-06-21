using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.ApiKeys;
using ShortLynx.Services.Domains;
using ShortLynx.Services.Links;

namespace ShortLynx.Tests.Admin;

internal sealed class FakeApiKeyService : IApiKeyService
{
    public readonly List<(string Name, string[] Scopes, Guid AccountId)> Created = [];
    public readonly List<(Guid KeyId, Guid AccountId)> Revoked = [];
    public string PlaintextToReturn = "PLAINTEXT-KEY-0123456789";

    public Task<(ApiKeyEntity Record, string PlaintextKey)> CreateAsync(
        string name, string[] scopes, Guid accountId, Guid? createdByUserAccountId = null, CancellationToken ct = default)
    {
        Created.Add((name, scopes, accountId));
        var rec = new ApiKeyEntity
        {
            Id = Guid.CreateVersion7(), Name = name, Prefix = "ABCDEF12", KeyHash = "h",
            Scopes = string.Join(",", scopes), CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true, AccountId = accountId, UserAccountId = createdByUserAccountId,
        };
        return Task.FromResult((rec, PlaintextToReturn));
    }

    public Task<ApiKeyEntity?> ValidateAsync(string plaintextKey, CancellationToken ct = default)
        => Task.FromResult<ApiKeyEntity?>(null);

    public Task<bool> RevokeAsync(Guid keyId, Guid accountId, CancellationToken ct = default)
    {
        Revoked.Add((keyId, accountId));
        return Task.FromResult(true);
    }
}

internal sealed class FakeCustomDomainService : ICustomDomainService
{
    public readonly List<CustomDomainEntity> Domains = [];
    public readonly List<(Guid Id, Guid Uid)> Verified = [];
    public readonly List<(Guid Id, Guid Uid)> Removed = [];
    public bool ThrowOnAdd;

    public Task<CustomDomainEntity> AddAsync(string domain, Guid accountId, Guid? addedByUserAccountId = null, CancellationToken ct = default)
    {
        if (ThrowOnAdd) throw new InvalidOperationException($"The domain '{domain}' is already registered.");
        var entity = new CustomDomainEntity
        {
            Id = Guid.CreateVersion7(), AccountId = accountId, UserAccountId = addedByUserAccountId, Domain = domain,
            CreatedAt = DateTimeOffset.UtcNow, IsActive = false,
            VerificationStatus = DomainVerificationStatus.Pending, VerificationToken = "tok-123",
        };
        Domains.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<IReadOnlyList<CustomDomainEntity>> ListAsync(Guid accountId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CustomDomainEntity>>(
            Domains.Where(d => d.AccountId == accountId).ToList());

    public Task<CustomDomainEntity?> VerifyAsync(Guid domainId, Guid userAccountId, CancellationToken ct = default)
    {
        Verified.Add((domainId, userAccountId));
        var d = Domains.FirstOrDefault(x => x.Id == domainId);
        if (d is not null)
        {
            d.VerificationStatus = DomainVerificationStatus.Verified;
            d.IsActive = true;
            d.VerifiedAt = DateTimeOffset.UtcNow;
        }
        return Task.FromResult(d);
    }

    public Task<bool> RemoveAsync(Guid domainId, Guid userAccountId, CancellationToken ct = default)
    {
        Removed.Add((domainId, userAccountId));
        return Task.FromResult(Domains.RemoveAll(d => d.Id == domainId) > 0);
    }

    public Task<int> RecheckVerifiedAsync(CancellationToken ct = default) => Task.FromResult(0);
}

internal sealed class FakeLinkService : ILinkService
{
    public readonly List<(string Url, Guid AccountId)> Created = [];
    public readonly List<(string Url, Guid AccountId)> CreatedUserAttributed = [];
    public readonly List<(Guid LinkId, IReadOnlyCollection<CodeRecipient> Recipients, bool OneTime)> Provisioned = [];
    public bool ThrowOnCreate;
    public string CodeToReturn = "abc12345";

    public Task<AnonymousLinkResult> CreateAnonymousLinkAsync(string url, ApiKeyEntity owner, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<AnonymousLinkResult> CreateAnonymousLinkAsync(string url, Guid accountId, Guid? createdByUserAccountId = null, CancellationToken ct = default)
    {
        if (ThrowOnCreate) throw new ArgumentException("blocked URL", nameof(url));
        Created.Add((url, accountId));
        var link = new LinkEntity
        {
            Id = Guid.CreateVersion7(), OriginalUrl = url, AccountId = accountId, UserAccountId = createdByUserAccountId,
            Mode = LinkMode.Anonymous, CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
        };
        var sc = new ShortCodeEntity
        {
            Id = Guid.CreateVersion7(), LinkId = link.Id, Code = CodeToReturn,
            CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
        };
        return Task.FromResult(new AnonymousLinkResult(link, sc));
    }

    public Task<LinkEntity> CreateUserAttributedLinkAsync(string url, Guid accountId, Guid? createdByUserAccountId = null, CancellationToken ct = default)
    {
        if (ThrowOnCreate) throw new ArgumentException("blocked URL", nameof(url));
        CreatedUserAttributed.Add((url, accountId));
        var link = new LinkEntity
        {
            Id = Guid.CreateVersion7(), OriginalUrl = url, AccountId = accountId, UserAccountId = createdByUserAccountId,
            Mode = LinkMode.UserAttributed, CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
        };
        return Task.FromResult(link);
    }

    public Task<IReadOnlyList<UserLinkCodeEntity>> CreateUserLinkCodesAsync(
        Guid linkId, IEnumerable<Guid> userIds, CancellationToken ct = default)
        => CreateUserLinkCodesAsync(linkId, userIds.Select(id => new CodeRecipient(id)).ToList(), false, ct);

    public readonly List<(Guid LinkId, Guid? DomainId, Guid AccountId)> DomainSet = [];

    public Task<bool> SetLinkDomainAsync(Guid linkId, Guid? customDomainId, Guid accountId, CancellationToken ct = default)
    {
        DomainSet.Add((linkId, customDomainId, accountId));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<UserLinkCodeEntity>> CreateUserLinkCodesAsync(
        Guid linkId, IReadOnlyCollection<CodeRecipient> recipients, bool isOneTimeUse, CancellationToken ct = default)
    {
        Provisioned.Add((linkId, recipients, isOneTimeUse));
        var i = 0;
        IReadOnlyList<UserLinkCodeEntity> codes = recipients.Select(r => new UserLinkCodeEntity
        {
            Id = Guid.CreateVersion7(), LinkId = linkId, UserId = r.UserId,
            Code = $"{CodeToReturn}{i++}", CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            IsOneTimeUse = isOneTimeUse, Recipient = r.Recipient,
        }).ToList();
        return Task.FromResult(codes);
    }
}
