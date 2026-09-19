using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TalentBridge.Post.Tests;

public sealed class HealthEndpointTests : IClassFixture<HealthEndpointTests.Factory>
{
    /// <summary>
    /// Boots the API with a connection string that satisfies startup validation
    /// but is never dialled: liveness must not touch the database.
    /// </summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Database:ConnectionString", "Host=localhost;Port=1;Database=unused;Username=u;Password=p");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        }
    }

    private readonly Factory _factory;

    public HealthEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Liveness_returns_200_without_a_database()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_reports_unhealthy_when_database_is_unreachable()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
