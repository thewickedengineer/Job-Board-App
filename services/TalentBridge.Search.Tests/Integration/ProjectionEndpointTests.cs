using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentBridge.Search.Api.Configuration;

namespace TalentBridge.Search.Tests.Integration;

[Collection(SearchPostgresCollection.Name)]
public sealed class ProjectionEndpointTests(SearchPostgresFixture postgres) : IDisposable
{
    private readonly SearchApiFactory _factory = new(postgres.ConnectionString);

    private HttpClient Client(string? secret)
    {
        var client = _factory.CreateClient();
        if (secret is not null)
        {
            client.DefaultRequestHeaders.Add(ProjectionOptions.HeaderName, secret);
        }

        return client;
    }

    [Fact]
    public async Task Health_endpoints_are_green()
    {
        using var client = Client(null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-secret")]
    public async Task Missing_or_wrong_secret_is_401(string? secret)
    {
        using var client = Client(secret);
        var response = await client.PostAsJsonAsync("/internal/projections/job", JobProjectionHandlerTests.Message(Guid.NewGuid(), 1));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Valid_secret_applies_then_reports_a_replay_as_not_applied_with_202_both_times()
    {
        using var client = Client(SearchApiFactory.TestSecret);
        var message = JobProjectionHandlerTests.Message(Guid.NewGuid(), 1);

        var first = await client.PostAsJsonAsync("/internal/projections/job", message);
        var second = await client.PostAsJsonAsync("/internal/projections/job", message);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.True((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applied").GetBoolean());
        Assert.False((await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applied").GetBoolean());
    }

    [Fact]
    public async Task Invalid_message_is_400_problem_details()
    {
        using var client = Client(SearchApiFactory.TestSecret);
        var response = await client.PostAsJsonAsync("/internal/projections/job", JobProjectionHandlerTests.Message(Guid.Empty, 0) with { Slug = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var keys = problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToArray();
        Assert.Equal(["id", "slug", "version"], keys);
    }

    public void Dispose() => _factory.Dispose();
}
