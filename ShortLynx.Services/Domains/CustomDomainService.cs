using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Domains;

public sealed class CustomDomainService(
    ShortLynxDbContext db,
    IDnsResolver dns,
    IOptions<CustomDomainOptions> options) : ICustomDomainService
{
    private readonly CustomDomainOptions _opts = options.Value;

    public async Task<CustomDomainEntity> AddAsync(string domain, Guid userAccountId, CancellationToken ct = default)
    {
        var normalised = Normalise(domain);
        if (normalised.Length == 0)
            throw new ArgumentException("Enter a domain.", nameof(domain));

        if (await db.CustomDomainEntities.AnyAsync(d => d.Domain == normalised, ct))
            throw new InvalidOperationException($"The domain '{normalised}' is already registered.");

        var entity = new CustomDomainEntity
        {
            Id = Guid.CreateVersion7(),
            UserAccountId = userAccountId,
            Domain = normalised,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = false,
            VerificationStatus = DomainVerificationStatus.Pending,
            VerificationToken = GenerateToken(),
        };

        db.CustomDomainEntities.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<CustomDomainEntity>> ListAsync(Guid userAccountId, CancellationToken ct = default)
        => await db.CustomDomainEntities
            .Where(d => d.UserAccountId == userAccountId)
            .OrderByDescending(d => d.Id)
            .ToListAsync(ct);

    public async Task<CustomDomainEntity?> VerifyAsync(Guid domainId, Guid userAccountId, CancellationToken ct = default)
    {
        var domain = await db.CustomDomainEntities
            .FirstOrDefaultAsync(d => d.Id == domainId && d.UserAccountId == userAccountId, ct);
        if (domain is null) return null;

        var host = _opts.VerificationHost(domain.Domain);
        var expected = _opts.ExpectedTxtValue(domain.VerificationToken);
        var records = await dns.GetTxtRecordsAsync(host, ct);

        var verified = records.Any(r => string.Equals(r.Trim(), expected, StringComparison.Ordinal));

        domain.VerificationStatus = verified ? DomainVerificationStatus.Verified : DomainVerificationStatus.Failed;
        domain.IsActive = verified;
        domain.VerifiedAt = verified ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync(ct);
        return domain;
    }

    public async Task<bool> RemoveAsync(Guid domainId, Guid userAccountId, CancellationToken ct = default)
    {
        var affected = await db.CustomDomainEntities
            .Where(d => d.Id == domainId && d.UserAccountId == userAccountId)
            .ExecuteDeleteAsync(ct);
        return affected > 0;
    }

    // Lowercases, trims, and strips any scheme/path the user may have pasted, leaving a bare host.
    private static string Normalise(string domain)
    {
        var value = domain.Trim().ToLowerInvariant();
        if (value.Length == 0) return value;

        if (Uri.TryCreate(value.Contains("://") ? value : $"//{value}", UriKind.RelativeOrAbsolute, out var uri)
            && uri.IsAbsoluteUri)
            value = uri.Host;
        else
            value = value.Split('/', 2)[0];

        return value.TrimEnd('.');
    }

    private static string GenerateToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
