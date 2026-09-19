using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TalentBridge.Post.Tests.Integration;

/// <summary>
/// Boots the real Post API against the test container. Every secret the app
/// fails-fast on is supplied here, so the suite never depends on a .env file.
/// </summary>
public sealed class PostApiFactory(string connectionString, IDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    public const string TestSigningSecret = "test-only-signing-secret-that-is-at-least-32-bytes";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:ConnectionString", connectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Jwt:SigningSecret", TestSigningSecret);
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4200");
        // Generous defaults so ordinary tests never trip the limiter; the
        // rate-limit test lowers them explicitly.
        builder.UseSetting("AuthRateLimit:LoginPermitLimit", "1000");
        builder.UseSetting("AuthRateLimit:SignupPermitLimit", "1000");

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }
    }
}
