using System.Net;
using System.Net.Http.Json;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Social;

/// <summary>
/// Bluesky (AT Protocol) connector. Connect = exchange handle + app password for a session via
/// <c>com.atproto.server.createSession</c>, which validates the credentials and yields the account's
/// stable DID, current handle, and access/refresh JWTs. Typed HttpClient — registered via
/// <c>AddHttpClient</c> in the composition root.
/// </summary>
public sealed class BlueskyConnector(HttpClient http) : ISocialConnector
{
    /// <summary>The default PDS host; self-hosted PDS users can override via InstanceUrl.</summary>
    public const string DefaultService = "https://bsky.social";

    public SocialPlatform Platform => SocialPlatform.Bluesky;

    public async Task<SocialIdentity> ConnectAsync(SocialCredentials credentials, CancellationToken ct = default)
    {
        var baseUrl = string.IsNullOrWhiteSpace(credentials.InstanceUrl)
            ? DefaultService
            : credentials.InstanceUrl.TrimEnd('/');

        using var response = await http.PostAsJsonAsync(
            $"{baseUrl}/xrpc/com.atproto.server.createSession",
            new { identifier = credentials.Identifier, password = credentials.Secret },
            ct);

        // Bluesky answers 400/401 for a bad identifier or app password — a user error, not a fault.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            throw new ArgumentException("Bluesky rejected the handle or app password.");

        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<CreateSessionResponse>(ct)
                      ?? throw new InvalidOperationException("Empty createSession response from Bluesky.");

        // The DID is the stable id (survives handle changes); the handle is for display.
        return new SocialIdentity(session.Did, session.Handle, session.AccessJwt, session.RefreshJwt, ExpiresAt: null);
    }

    private sealed record CreateSessionResponse(string Did, string Handle, string AccessJwt, string? RefreshJwt);
}
