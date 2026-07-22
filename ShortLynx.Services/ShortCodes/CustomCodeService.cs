using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;

namespace ShortLynx.Services.ShortCodes;

public enum CodeAvailabilityStatus { Available, Taken, Invalid }

/// <summary>Result of a custom-code availability check. <see cref="Reason"/> is null when available.</summary>
public sealed record CodeAvailability(CodeAvailabilityStatus Status, string? Reason)
{
    public bool IsAvailable => Status == CodeAvailabilityStatus.Available;
}

public interface ICustomCodeService
{
    /// <summary>
    /// Validates a proposed custom code and reports whether it's free to claim. Advisory only — the
    /// authoritative uniqueness is the DB unique index at insert time (handles the check→create race).
    /// </summary>
    Task<CodeAvailability> CheckAsync(string? rawCode, CancellationToken ct = default);
}

public sealed class CustomCodeService(ShortLynxDbContext db, CustomCodeValidator validator) : ICustomCodeService
{
    public async Task<CodeAvailability> CheckAsync(string? rawCode, CancellationToken ct = default)
    {
        var validation = validator.Validate(rawCode);
        if (!validation.IsValid)
            return new CodeAvailability(CodeAvailabilityStatus.Invalid, validation.Reason);

        // Custom codes share the ShortCodes.Code unique-index namespace with generated codes (they're
        // isolated from Mode-2/social codes by route, not table — see CUSTOM_CODE_PLAN §3), so one
        // existence query on the normalized code is sufficient.
        var code = CustomCodeValidator.Normalize(rawCode!);
        var taken = await db.ShortCodeEntities.AnyAsync(sc => sc.Code == code, ct);
        return taken
            ? new CodeAvailability(CodeAvailabilityStatus.Taken, "That code is already taken.")
            : new CodeAvailability(CodeAvailabilityStatus.Available, null);
    }
}
