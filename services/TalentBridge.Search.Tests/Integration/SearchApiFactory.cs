using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TalentBridge.Search.Tests.Integration;

public sealed class SearchApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string TestSecret = "test-only-projection-secret-at-least-32-bytes";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:ConnectionString", connectionString);
        builder.UseSetting("Database:ApplySchemaOnStartup", "true");
        builder.UseSetting("Projection:SharedSecret", TestSecret);
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4201");
    }
}
