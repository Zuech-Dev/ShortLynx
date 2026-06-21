namespace ShortLynx.Services.Domains;

/// <summary>Settings for custom-domain verification. Bound from the "CustomDomain" section.</summary>
public sealed class CustomDomainOptions
{
    /// <summary>
    /// Subdomain label the user creates the TXT record under (e.g. <c>_shortlynx-verify</c> →
    /// the record is looked up at <c>_shortlynx-verify.go.example.com</c>).
    /// </summary>
    public string VerificationHostLabel { get; set; } = "_shortlynx-verify";

    /// <summary>Prefix prepended to the token in the published TXT value (e.g. <c>shortlynx-verify=</c>).</summary>
    public string TxtValuePrefix { get; set; } = "shortlynx-verify=";

    /// <summary>The exact TXT host the user must create the record at, for a given domain.</summary>
    public string VerificationHost(string domain) => $"{VerificationHostLabel}.{domain}";

    /// <summary>The exact TXT value the user must publish, for a given verification token.</summary>
    public string ExpectedTxtValue(string token) => $"{TxtValuePrefix}{token}";
}
