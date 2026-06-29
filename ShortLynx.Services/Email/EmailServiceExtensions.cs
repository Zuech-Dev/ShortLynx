using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ShortLynx.Services.Email;

public static class EmailServiceExtensions
{
    /// <summary>
    /// Registers the email sender per the "Email:Mode" setting: Resend (default), Log (dev — log every
    /// message), or Hybrid (Resend for "Email:DeliverableDomains", logged for the rest). Binds Resend +
    /// delivery options. Replaces a bare <c>AddHttpClient&lt;IEmailSender, ResendEmailSender&gt;()</c>.
    /// </summary>
    public static IServiceCollection AddShortLynxEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ResendOptions>(configuration.GetSection("Resend"));
        services.Configure<EmailDeliveryOptions>(configuration.GetSection("Email"));

        services.AddHttpClient<ResendEmailSender>();
        services.AddTransient<LoggingEmailSender>();

        var mode = configuration.GetValue<string>("Email:Mode") ?? "Resend";
        switch (mode.Trim().ToLowerInvariant())
        {
            case "log":
                services.AddTransient<IEmailSender>(sp => sp.GetRequiredService<LoggingEmailSender>());
                break;
            case "hybrid":
                services.AddTransient<IEmailSender>(sp => new RoutingEmailSender(
                    sp.GetRequiredService<ResendEmailSender>(),
                    sp.GetRequiredService<LoggingEmailSender>(),
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailDeliveryOptions>>()));
                break;
            default: // resend
                services.AddTransient<IEmailSender>(sp => sp.GetRequiredService<ResendEmailSender>());
                break;
        }

        return services;
    }
}
