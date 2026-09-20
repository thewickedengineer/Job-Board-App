using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentBridge.Search.Api.Configuration;
using TalentBridge.Search.Api.Jobs;
using TalentBridge.Search.Infrastructure.Projections;

namespace TalentBridge.Search.Tests.Integration;

/// <summary>
/// Drives the public read API end to end: rows go in through the projection
/// endpoint (so cache eviction is exercised too) and come out through
/// /api/jobs, /api/jobs/{slug} and /api/facets.
/// </summary>
[Collection(SearchPostgresCollection.Name)]
public sealed class JobEndpointsTests : IAsyncLifetime, IDisposable
{
    private readonly SearchApiFactory _factory;
    private readonly HttpClient _client;

    // Unique per run so rows from other test classes in the shared database never match.
    private readonly string _dept = $"Dept{Guid.NewGuid():N}"[..12];
    private readonly string _keyword = $"zq{Guid.NewGuid():N}"[..10];

    private JobProjectionMessage _open = null!;
    private JobProjectionMessage _hiddenSalary = null!;
    private JobProjectionMessage _closed = null!;
    private JobProjectionMessage _expired = null!;

    public JobEndpointsTests(SearchPostgresFixture postgres)
    {
        _factory = new SearchApiFactory(postgres.ConnectionString);
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add(ProjectionOptions.HeaderName, SearchApiFactory.TestSecret);
    }

    public async Task InitializeAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var baseMessage = JobProjectionHandlerTests.Message(Guid.NewGuid(), 1) with { Department = _dept, Organization = "TestOrg" };

        _open = baseMessage with
        {
            Id = Guid.NewGuid(), Title = $"{_keyword} Alpha Supervisor", Skills = ["Kryptonite", "WMS"], WorkArrangement = "Hybrid",
            SalaryMin = 50_000, SalaryMax = 60_000, PublishedAt = DateTimeOffset.UtcNow, ClosingDate = today.AddDays(3),
        };
        _hiddenSalary = baseMessage with
        {
            Id = Guid.NewGuid(), Title = $"{_keyword} Beta Analyst", SalaryVisible = false, WorkArrangement = "OnSite",
            SalaryMin = 90_000, SalaryMax = 95_000, PublishedAt = DateTimeOffset.UtcNow.AddDays(-10), ClosingDate = today.AddDays(30),
        };
        _closed = baseMessage with { Id = Guid.NewGuid(), Title = $"{_keyword} Gamma Closed", IsOpen = false };
        _expired = baseMessage with { Id = Guid.NewGuid(), Title = $"{_keyword} Delta Expired", ClosingDate = today.AddDays(-1) };

        _open = _open with { Slug = SlugFor(_open) };
        _hiddenSalary = _hiddenSalary with { Slug = SlugFor(_hiddenSalary) };
        _closed = _closed with { Slug = SlugFor(_closed) };
        _expired = _expired with { Slug = SlugFor(_expired) };

        foreach (var m in new[] { _open, _hiddenSalary, _closed, _expired })
        {
            var response = await _client.PostAsJsonAsync("/internal/projections/job", m);
            response.EnsureSuccessStatusCode();
        }
    }

    private static string SlugFor(JobProjectionMessage m) => $"{m.Title.ToLowerInvariant().Replace(' ', '-')}-{m.Id:N}";

    private Task<PagedResponse<JobSummaryResponse>?> ListAsync(string query) =>
        _client.GetFromJsonAsync<PagedResponse<JobSummaryResponse>>($"/api/jobs?{query}");

    [Fact]
    public async Task List_shows_only_open_unexpired_postings_and_withholds_hidden_salaries()
    {
        var page = (await ListAsync($"department={_dept}"))!;

        Assert.Equal(2, page.Total);
        var titles = page.Items.Select(i => i.Title).ToArray();
        Assert.Contains(_open.Title, titles);
        Assert.Contains(_hiddenSalary.Title, titles);
        Assert.DoesNotContain(_closed.Title, titles);
        Assert.DoesNotContain(_expired.Title, titles);

        var hidden = page.Items.Single(i => i.Title == _hiddenSalary.Title);
        Assert.False(hidden.SalaryVisible);
        Assert.Null(hidden.SalaryMin);
        Assert.Null(hidden.SalaryMax);
        Assert.Equal(JobListQueryExcerpt(), hidden.Excerpt.Length);

        // Newest first by default.
        Assert.Equal(_open.Title, page.Items[0].Title);
    }

    private int JobListQueryExcerpt() => Math.Min(_hiddenSalary.Description.Length, Infrastructure.Queries.JobListQuery.ExcerptLength);

    [Fact]
    public async Task Keyword_search_matches_skills_and_relevance_sort_is_accepted()
    {
        var bySkill = (await ListAsync("q=kryptonite&sort=relevance"))!;
        Assert.Single(bySkill.Items, i => i.Id == _open.Id);

        var byTitle = (await ListAsync($"q={_keyword}&department={_dept}"))!;
        Assert.Equal(2, byTitle.Total);
    }

    [Fact]
    public async Task Filters_combine_and_a_salary_filter_never_matches_a_hidden_salary()
    {
        var hybrid = (await ListAsync($"department={_dept}&workArrangement=Hybrid&workArrangement=Remote"))!;
        Assert.Equal([_open.Id], hybrid.Items.Select(i => i.Id));

        var recent = (await ListAsync($"department={_dept}&postedWithinDays=1"))!;
        Assert.Equal([_open.Id], recent.Items.Select(i => i.Id));

        // The hidden-salary row pays 90k, but probing with a salary filter must not reveal that.
        var rich = (await ListAsync($"department={_dept}&salaryMin=80000"))!;
        Assert.Empty(rich.Items);

        var affordable = (await ListAsync($"department={_dept}&salaryMin=55000&salaryMax=70000"))!;
        Assert.Equal([_open.Id], affordable.Items.Select(i => i.Id));

        var paged = (await ListAsync($"department={_dept}&pageSize=1&page=2&sort=closingSoon"))!;
        Assert.Equal(2, paged.Total);
        Assert.Equal(2, paged.TotalPages);
        Assert.Equal([_hiddenSalary.Id], paged.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Invalid_query_parameters_are_400_with_camelCase_keys()
    {
        var response = await _client.GetAsync("/api/jobs?sort=newest&pageSize=51&page=0&postedWithinDays=0&salaryMin=5&salaryMax=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var keys = problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToArray();
        Assert.Equal(["page", "pageSize", "postedWithinDays", "salaryMax", "sort"], keys);
    }

    [Fact]
    public async Task Detail_returns_the_full_posting_similar_roles_an_etag_and_304_on_match()
    {
        var page = (await ListAsync($"department={_dept}"))!;
        var slug = page.Items.Single(i => i.Id == _open.Id).Slug;

        var response = await _client.GetAsync($"/api/jobs/{slug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl?.ToString());
        var etag = response.Headers.ETag;
        Assert.NotNull(etag);

        var detail = (await response.Content.ReadFromJsonAsync<JobDetailResponse>())!;
        Assert.Equal(_open.Title, detail.Title);
        Assert.True(detail.IsOpen);
        Assert.Equal(["Kryptonite", "WMS"], detail.Skills);
        Assert.Equal(50_000, detail.SalaryMin);
        Assert.DoesNotContain(detail.Similar, s => s.Slug == slug);
        Assert.Contains(detail.Similar, s => s.Title == _hiddenSalary.Title); // same department ranks first
        Assert.Null(detail.Similar.Single(s => s.Title == _hiddenSalary.Title).SalaryMin);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/jobs/{slug}");
        conditional.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.NotModified, (await _client.SendAsync(conditional)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/jobs/never-existed")).StatusCode);
    }

    [Fact]
    public async Task Closed_posting_still_renders_with_isOpen_false()
    {
        var response = await _client.GetAsync($"/api/jobs/{_closed.Slug}");
        var detail = (await response.Content.ReadFromJsonAsync<JobDetailResponse>())!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(detail.IsOpen);
    }

    [Fact]
    public async Task Facets_count_each_dimension_with_its_own_filter_excluded()
    {
        var facets = (await _client.GetFromJsonAsync<FacetsResponse>($"/api/facets?department={_dept}&workArrangement=Remote"))!;

        // Total honours every filter: nothing in this department is Remote.
        Assert.Equal(0, facets.Total);
        // Work-arrangement counts ignore the Remote filter so the user can switch.
        Assert.Equal(1, facets.WorkArrangements.Single(f => f.Value == "Hybrid").Count);
        Assert.Equal(1, facets.WorkArrangements.Single(f => f.Value == "OnSite").Count);
        // Department counts ignore the department filter but keep the Remote one.
        Assert.DoesNotContain(facets.Departments, f => f.Value == _dept);
    }

    [Fact]
    public async Task A_new_projection_evicts_the_output_cache_so_the_list_reflects_it_immediately()
    {
        var before = (await ListAsync($"department={_dept}"))!;
        var fresh = _open with { Id = Guid.NewGuid(), Title = $"{_keyword} Epsilon Fresh", Slug = $"epsilon-{Guid.NewGuid():N}" };
        (await _client.PostAsJsonAsync("/internal/projections/job", fresh)).EnsureSuccessStatusCode();

        var after = (await ListAsync($"department={_dept}"))!;

        Assert.Equal(before.Total + 1, after.Total);
        Assert.Contains(after.Items, i => i.Id == fresh.Id);
        Assert.Equal("public, max-age=60", (await _client.GetAsync($"/api/jobs?department={_dept}")).Headers.CacheControl?.ToString());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
