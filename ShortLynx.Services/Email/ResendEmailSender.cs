using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ShortLynx.Services.Email;

/// <summary>
/// Sends email via the Resend HTTP API (https://resend.com). Registered as a typed HttpClient.
/// </summary>
public sealed class ResendEmailSender(HttpClient http, IOptions<ResendOptions> options) : IEmailSender
{
    private const string Endpoint = "https://api.resend.com/emails";

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var opts = options.Value;
        var from = string.IsNullOrWhiteSpace(opts.FromName)
            ? opts.FromAddress
            : $"{opts.FromName} <{opts.FromAddress}>";

        var payload = JsonSerializer.Serialize(new
        {
            from,
            to = new[] { to },
            subject,
            html = htmlBody,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Resend API returned {(int)response.StatusCode} ({response.StatusCode}): {body}");
        }
    }
}
