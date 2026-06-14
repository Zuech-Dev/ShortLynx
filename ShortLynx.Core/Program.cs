using ShortLynx.Core.Auth;
using ShortLynx.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShortLynxDatabase(builder.Configuration);
builder.Services.AddShortLynxServices(builder.Configuration);
builder.Services.AddShortLynxRateLimiting(builder.Configuration);

builder.Services
    .AddAuthentication(ApiKeyAuthHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthHandler>(
        ApiKeyAuthHandler.SchemeName, null);
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
