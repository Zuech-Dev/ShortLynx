using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Sentry;
using Serilog;
using Serilog.Formatting.Elasticsearch;
using Serilog.Sinks.Http;

namespace ShortLynx.Services.Observability;

/// <summary>
/// Shared observability wiring, split across two call sites for a reason: <see cref="AddShortLynxLogging"/>
/// (Serilog console + optional Axiom) only touches <c>IHostBuilder</c> types from
/// <c>Microsoft.Extensions.Hosting.Abstractions</c>, which resolve fine from a plain class library. Sentry's
/// <c>UseSentry()</c> needs the full ASP.NET Core shared-framework type identity that only a
/// <c>Microsoft.NET.Sdk.Web</c> project has (confirmed by an isolated test — a <c>FrameworkReference</c>
/// override on a plain <c>Microsoft.NET.Sdk</c> library does not reproduce it), so that call lives
/// directly in each app's own <c>Program.cs</c> instead; <see cref="ScrubSensitiveData"/> is exposed here
/// so the scrubbing rule itself is still written once.
///
/// Both pieces are inert unless explicitly configured — a self-hoster who sets nothing gets exactly
/// today's behavior (console logging only, no outbound telemetry). Mirrors the same "silently absent is
/// fine" shape as <c>Resend:ApiKey</c> (<see cref="ShortLynx.Services.Email.ResendOptions"/>) rather than
/// the hard-fail-at-boot shape used for Jwt:SigningKey/ApiKey:HmacSecret — this is optional tooling, not
/// core function, so there's no <c>.ValidateOnStart()</c> and no <c>?? throw</c> anywhere in this file.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>Console logging (unchanged) plus optional structured shipping to Axiom.</summary>
    public static WebApplicationBuilder AddShortLynxLogging(this WebApplicationBuilder builder)
    {
        var axiomToken = builder.Configuration["Axiom:ApiToken"];
        var axiomDataset = builder.Configuration["Axiom:Dataset"];
        builder.Host.UseSerilog((ctx, cfg) =>
        {
            cfg.Enrich.FromLogContext()
               .Enrich.WithEnvironmentName()
               .Enrich.WithProperty("service", ctx.HostingEnvironment.ApplicationName)
               .WriteTo.Console(); // unchanged existing behavior — console stays authoritative either way

            if (!string.IsNullOrWhiteSpace(axiomToken) && !string.IsNullOrWhiteSpace(axiomDataset))
            {
                cfg.WriteTo.Http(
                    requestUri: $"https://api.axiom.co/v1/datasets/{axiomDataset}/ingest",
                    queueLimitBytes: null,
                    textFormatter: new ElasticsearchJsonFormatter(renderMessageTemplate: false, inlineFields: true),
                    httpClient: new BearerHttpClient(axiomToken));
            }
        });

        return builder;
    }

    /// <summary>
    /// This app's actual token/secret surface: magic-link and OAuth-state tokens travel as query
    /// params, CSRF/API-key/session credentials as headers. Stripped before an event leaves the
    /// process — defense in depth on top of SendDefaultPii=false, not a replacement for it. Called
    /// from each Program.cs's own <c>UseSentry(o => o.SetBeforeSend(...))</c>.
    /// </summary>
    public static SentryEvent? ScrubSensitiveData(SentryEvent @event)
    {
        if (!string.IsNullOrEmpty(@event.Request.QueryString))
            @event.Request.QueryString = "[Scrubbed]";

        foreach (var header in new[] { "Authorization", "X-Csrf-Token", "X-Api-Key", "Cookie" })
            @event.Request.Headers.Remove(header);

        return @event;
    }

    /// <summary>Minimal Serilog.Sinks.Http client that sends every request with a fixed Bearer token.</summary>
    private sealed class BearerHttpClient(string token) : IHttpClient
    {
        private readonly HttpClient _http = new();

        public void Configure(IConfiguration configuration)
        {
        }

        public async Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream, CancellationToken cancellationToken = default)
        {
            using var content = new StreamContent(contentStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() => _http.Dispose();
    }
}
