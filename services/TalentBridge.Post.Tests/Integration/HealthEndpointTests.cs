using System.Net;

namespace TalentBridge.Post.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class HealthEndpointTests(PostgresFixture postgres) : IDisposable
{
    private readonly PostApiFactory _factory = new(postgres.ConnectionString);

    [Fact]
    public async Task Liveness_and_readiness_are_healthy_with_a_database()
    {
        using var client = _factory.CreateClient();

        var live = await client.GetAsync("/health");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task Readiness_is_unhealthy_when_the_database_is_unreachable()
    {
        using var broken = new PostApiFactory(
            "Host=localhost;Port=1;Database=unused;Username=u;Password=p;Timeout=1",
            new Dictionary<string, string?> { ["Database:ApplyMigrationsOnStartup"] = "false" });
        using var client = broken.CreateClient();

        var live = await client.GetAsync("/health");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
