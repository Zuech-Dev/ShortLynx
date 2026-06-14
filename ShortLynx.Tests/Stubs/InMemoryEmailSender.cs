using ShortLynx.Services.Email;

namespace ShortLynx.Tests.Stubs;

public sealed class InMemoryEmailSender : IEmailSender
{
    public List<(string To, string Subject, string HtmlBody)> Sent { get; } = [];

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        Sent.Add((to, subject, htmlBody));
        return Task.CompletedTask;
    }
}
