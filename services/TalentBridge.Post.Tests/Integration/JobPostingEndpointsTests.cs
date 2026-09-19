using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TalentBridge.Post.Api.Auth;
using TalentBridge.Post.Api.JobPostings;
using TalentBridge.Post.Domain.Outbox;
using TalentBridge.Post.Infrastructure.Persistence;

namespace TalentBridge.Post.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class JobPostingEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly PostApiFactory _factory = new(postgres.ConnectionString);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>A fresh manager with a bearer token already attached.</summary>
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = _factory.CreateClient(new() { HandleCookies = false });
        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new SignupRequest($"dana.{Guid.NewGuid():N}@northline.co", "CorrectHorse42", "Dana Whitfield", "Northline"));
        signup.EnsureSuccessStatusCode();
        var auth = (await signup.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private static CreateJobPostingRequest ValidRequest(string? referenceCode = null) => new()
    {
        ReferenceCode = referenceCode,
        Title = "Senior Warehouse Supervisor",
        Department = "Operations",
        EmploymentType = "FullTime",
        Seniority = "Senior",
        Openings = 2,
        WorkArrangement = "Hybrid",
        Location = "Leeds, West Yorkshire",
        Country = "United Kingdom",
        SalaryMin = 38_000,
        SalaryMax = 46_000,
        SalaryCurrency = "GBP",
        PayPeriod = "Annual",
        SalaryVisible = true,
        Description = "Northline's Leeds distribution hub handles 40,000 outbound units a week. You'll own the evening shift.",
        Responsibilities = "Run the evening shift\nCoach team leads",
        Skills = ["Warehouse ops", "WMS", "Team leadership"],
        ApplicationEmail = "careers@northline.co",
        ClosingDate = Today.AddDays(14),
    };

    private async Task<int> OutboxCountAsync(Guid postingId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostDbContext>();
        return await db.OutboxMessages.CountAsync(m => m.AggregateId == postingId);
    }

    // --- create ---------------------------------------------------------------

    [Fact]
    public async Task Create_returns_201_with_the_complete_persisted_record_and_an_outbox_row()
    {
        using var client = await AuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/job-postings", ValidRequest("REF-4471"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JobPostingResponse>())!;

        Assert.Equal($"/api/job-postings/{body.Id}", response.Headers.Location?.ToString());
        Assert.NotEqual(Guid.Empty, body.Id);
        // Other tests in this shared database create the same title/location; the
        // base slug is fixed and any collision gets a numeric suffix.
        Assert.Matches("^senior-warehouse-supervisor-leeds(-[0-9]+)?$", body.Slug);
        Assert.Equal("Published", body.Status);
        Assert.True(body.IsOpen);
        Assert.Equal(1, body.Version);
        Assert.NotNull(body.PublishedAt);
        Assert.Equal(body.CreatedAt, body.UpdatedAt);
        Assert.Equal("REF-4471", body.ReferenceCode);
        Assert.Equal(["Warehouse ops", "WMS", "Team leadership"], body.Skills);

        // The projection message rides in the same transaction as the posting.
        Assert.Equal(1, await OutboxCountAsync(body.Id));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PostDbContext>();
        var outbox = await db.OutboxMessages.SingleAsync(m => m.AggregateId == body.Id);
        Assert.Equal(JobProjectionMessage.MessageType, outbox.Type);
        Assert.Null(outbox.ProcessedAt);
        var payload = JsonSerializer.Deserialize<JobProjectionMessage>(outbox.Payload, JsonSerializerOptions.Web)!;
        Assert.Equal("Northline", payload.Organization);
        Assert.Equal(1, payload.Version);
        Assert.True(payload.IsOpen);
    }

    [Fact]
    public async Task Create_with_invalid_body_returns_400_with_the_exact_camelCase_keys()
    {
        using var client = await AuthenticatedClientAsync();
        var invalid = ValidRequest() with
        {
            Title = "Ab",
            SalaryMin = 46_000,
            SalaryMax = 38_000,
            ClosingDate = Today,
            EmploymentType = "Gig",
            ApplicationUrl = "northline.co",
            Skills = Enumerable.Range(1, 21).Select(i => $"s{i}").ToArray(),
        };

        var response = await client.PostAsJsonAsync("/api/job-postings", invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.1", problem.GetProperty("type").GetString());
        Assert.Equal("One or more validation errors occurred.", problem.GetProperty("title").GetString());
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("traceId", out _));

        var errors = problem.GetProperty("errors");
        var keys = errors.EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToArray();
        Assert.Equal(["applicationUrl", "closingDate", "employmentType", "salaryMax", "skills", "title"], keys);
        Assert.Equal("Salary maximum must be greater than salary minimum.", errors.GetProperty("salaryMax")[0].GetString());
        Assert.Equal("Closing date must be in the future.", errors.GetProperty("closingDate")[0].GetString());
    }

    [Fact]
    public async Task Create_requires_authentication()
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/job-postings", ValidRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_as_draft_is_not_projected_and_reports_draft()
    {
        using var client = await AuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/job-postings", ValidRequest() with { Status = "Draft" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JobPostingResponse>())!;
        Assert.Equal("Draft", body.Status);
        Assert.False(body.IsOpen);
        Assert.Null(body.PublishedAt);
        Assert.Equal(0, await OutboxCountAsync(body.Id));
    }

    [Fact]
    public async Task Create_for_remote_role_uses_the_country_as_location_and_suffixes_duplicate_slugs()
    {
        using var client = await AuthenticatedClientAsync();
        var remote = ValidRequest() with { Title = "Logistics Data Analyst", WorkArrangement = "Remote", Location = null, Country = "United Kingdom" };

        var first = (await (await client.PostAsJsonAsync("/api/job-postings", remote)).Content.ReadFromJsonAsync<JobPostingResponse>())!;
        var second = (await (await client.PostAsJsonAsync("/api/job-postings", remote)).Content.ReadFromJsonAsync<JobPostingResponse>())!;

        Assert.Equal("United Kingdom", first.Location);
        Assert.Equal("logistics-data-analyst-united-kingdom", first.Slug);
        Assert.Equal("logistics-data-analyst-united-kingdom-2", second.Slug);
    }

    [Fact]
    public async Task Create_rejects_a_reference_code_already_used_by_the_same_manager()
    {
        using var client = await AuthenticatedClientAsync();
        await client.PostAsJsonAsync("/api/job-postings", ValidRequest("REF-1"));

        var response = await client.PostAsJsonAsync("/api/job-postings", ValidRequest("REF-1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("REF-1 is already used", problem.GetProperty("errors").GetProperty("referenceCode")[0].GetString());

        // A different manager may reuse the same code.
        using var other = await AuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/api/job-postings", ValidRequest("REF-1"))).StatusCode);
    }

    // --- list / get ------------------------------------------------------------

    [Fact]
    public async Task List_returns_only_the_callers_postings_newest_first_and_paged()
    {
        using var mine = await AuthenticatedClientAsync();
        using var theirs = await AuthenticatedClientAsync();
        for (var i = 1; i <= 3; i++)
        {
            await mine.PostAsJsonAsync("/api/job-postings", ValidRequest() with { Title = $"Mine {i}" });
        }

        await theirs.PostAsJsonAsync("/api/job-postings", ValidRequest() with { Title = "Not mine" });

        var page1 = (await mine.GetFromJsonAsync<PagedResponse<JobPostingSummary>>("/api/job-postings?pageSize=2"))!;
        var page2 = (await mine.GetFromJsonAsync<PagedResponse<JobPostingSummary>>("/api/job-postings?pageSize=2&page=2"))!;

        Assert.Equal(3, page1.Total);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(["Mine 3", "Mine 2"], page1.Items.Select(i => i.Title));
        Assert.Equal(["Mine 1"], page2.Items.Select(i => i.Title));
        Assert.DoesNotContain(page1.Items.Concat(page2.Items), i => i.Title == "Not mine");
    }

    [Fact]
    public async Task List_filters_by_search_text_and_status_and_rejects_unknown_sort()
    {
        using var client = await AuthenticatedClientAsync();
        await client.PostAsJsonAsync("/api/job-postings", ValidRequest("REF-9001") with { Title = "Fleet Technician" });
        await client.PostAsJsonAsync("/api/job-postings", ValidRequest() with { Title = "Data Analyst", Department = "Data & Insight", Status = "Draft" });

        var byRef = (await client.GetFromJsonAsync<PagedResponse<JobPostingSummary>>("/api/job-postings?q=9001"))!;
        Assert.Equal(["Fleet Technician"], byRef.Items.Select(i => i.Title));

        var byDept = (await client.GetFromJsonAsync<PagedResponse<JobPostingSummary>>("/api/job-postings?q=insight"))!;
        Assert.Equal(["Data Analyst"], byDept.Items.Select(i => i.Title));

        var drafts = (await client.GetFromJsonAsync<PagedResponse<JobPostingSummary>>("/api/job-postings?status=Draft"))!;
        Assert.Equal(["Data Analyst"], drafts.Items.Select(i => i.Title));

        var published = (await client.GetFromJsonAsync<PagedResponse<JobPostingSummary>>("/api/job-postings?status=Published&sort=titleAsc"))!;
        Assert.Equal(["Fleet Technician"], published.Items.Select(i => i.Title));

        var badSort = await client.GetAsync("/api/job-postings?sort=newest");
        Assert.Equal(HttpStatusCode.BadRequest, badSort.StatusCode);
        var problem = await badSort.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("sort", out _));

        var tooBig = await client.GetAsync("/api/job-postings?pageSize=51");
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
    }

    [Fact]
    public async Task Get_returns_own_posting_and_404_for_someone_elses()
    {
        using var owner = await AuthenticatedClientAsync();
        using var stranger = await AuthenticatedClientAsync();
        var created = (await (await owner.PostAsJsonAsync("/api/job-postings", ValidRequest())).Content.ReadFromJsonAsync<JobPostingResponse>())!;

        var own = await owner.GetAsync($"/api/job-postings/{created.Id}");
        var foreign = await stranger.GetAsync($"/api/job-postings/{created.Id}");
        var missing = await owner.GetAsync($"/api/job-postings/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        var fetched = (await own.Content.ReadFromJsonAsync<JobPostingResponse>())!;
        // Records compare arrays by reference, so compare Skills separately.
        var none = Array.Empty<string>();
        Assert.Equal(created with { Skills = none }, fetched with { Skills = none });
        Assert.Equal(created.Skills, fetched.Skills);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    // --- update ---------------------------------------------------------------

    [Fact]
    public async Task Update_replaces_content_bumps_version_keeps_slug_and_enqueues_a_projection()
    {
        using var client = await AuthenticatedClientAsync();
        var created = (await (await client.PostAsJsonAsync("/api/job-postings", ValidRequest())).Content.ReadFromJsonAsync<JobPostingResponse>())!;

        var update = new UpdateJobPostingRequest
        {
            Version = created.Version,
            Title = "Warehouse Shift Lead",
            Department = created.Department,
            EmploymentType = created.EmploymentType,
            Seniority = "Lead",
            Openings = 3,
            WorkArrangement = created.WorkArrangement,
            Location = created.Location,
            Country = created.Country,
            SalaryMin = 40_000,
            SalaryMax = 48_000,
            SalaryCurrency = created.SalaryCurrency,
            PayPeriod = created.PayPeriod,
            Description = created.Description,
            Skills = ["WMS"],
            ClosingDate = created.ClosingDate.AddDays(7),
        };

        var response = await client.PutAsJsonAsync($"/api/job-postings/{created.Id}", update);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JobPostingResponse>())!;
        Assert.Equal("Warehouse Shift Lead", body.Title);
        Assert.Equal(2, body.Version);
        Assert.Equal(created.Slug, body.Slug);
        Assert.Equal(created.PublishedAt, body.PublishedAt);
        Assert.True(body.UpdatedAt > created.UpdatedAt);
        Assert.Equal(2, await OutboxCountAsync(created.Id));
    }

    [Fact]
    public async Task Update_with_a_stale_version_returns_409()
    {
        using var client = await AuthenticatedClientAsync();
        var created = (await (await client.PostAsJsonAsync("/api/job-postings", ValidRequest())).Content.ReadFromJsonAsync<JobPostingResponse>())!;
        var update = ToUpdate(created) with { Title = "First edit" };
        await client.PutAsJsonAsync($"/api/job-postings/{created.Id}", update);

        // Same version again: the client is editing a stale copy.
        var stale = await client.PutAsJsonAsync($"/api/job-postings/{created.Id}", update with { Title = "Second edit" });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("version 2", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Update_publishes_a_draft_and_cannot_revert_a_published_posting_to_draft()
    {
        using var client = await AuthenticatedClientAsync();
        var draft = (await (await client.PostAsJsonAsync("/api/job-postings", ValidRequest() with { Status = "Draft" })).Content.ReadFromJsonAsync<JobPostingResponse>())!;

        var published = await client.PutAsJsonAsync($"/api/job-postings/{draft.Id}", ToUpdate(draft) with { Status = "Published" });
        var body = (await published.Content.ReadFromJsonAsync<JobPostingResponse>())!;
        Assert.Equal("Published", body.Status);
        Assert.NotNull(body.PublishedAt);
        Assert.Equal(1, await OutboxCountAsync(draft.Id));

        var revert = await client.PutAsJsonAsync($"/api/job-postings/{draft.Id}", ToUpdate(body) with { Status = "Draft" });
        Assert.Equal(HttpStatusCode.BadRequest, revert.StatusCode);
        var problem = await revert.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("status", out _));
    }

    [Fact]
    public async Task Update_without_version_returns_400_keyed_to_version()
    {
        using var client = await AuthenticatedClientAsync();
        var created = (await (await client.PostAsJsonAsync("/api/job-postings", ValidRequest())).Content.ReadFromJsonAsync<JobPostingResponse>())!;

        var response = await client.PutAsJsonAsync($"/api/job-postings/{created.Id}", ToUpdate(created) with { Version = null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("version", out _));
    }

    // --- close ----------------------------------------------------------------

    [Fact]
    public async Task Close_is_irreversible_projects_once_and_blocks_further_edits()
    {
        using var client = await AuthenticatedClientAsync();
        var created = (await (await client.PostAsJsonAsync("/api/job-postings", ValidRequest())).Content.ReadFromJsonAsync<JobPostingResponse>())!;

        var closed = await client.PostAsync($"/api/job-postings/{created.Id}/close", content: null);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var body = (await closed.Content.ReadFromJsonAsync<JobPostingResponse>())!;
        Assert.Equal("Closed", body.Status);
        Assert.False(body.IsOpen);
        Assert.Equal(2, body.Version);
        Assert.Equal(2, await OutboxCountAsync(created.Id));

        // Closing again is a no-op 200, not another version or message.
        var again = (await (await client.PostAsync($"/api/job-postings/{created.Id}/close", content: null)).Content.ReadFromJsonAsync<JobPostingResponse>())!;
        Assert.Equal(2, again.Version);
        Assert.Equal(2, await OutboxCountAsync(created.Id));

        var edit = await client.PutAsJsonAsync($"/api/job-postings/{created.Id}", ToUpdate(again) with { Title = "Reopened?" });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);

        using var stranger = await AuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/job-postings/{created.Id}/close", content: null)).StatusCode);
    }

    private static UpdateJobPostingRequest ToUpdate(JobPostingResponse p) => new()
    {
        Version = p.Version,
        ReferenceCode = p.ReferenceCode,
        Title = p.Title,
        Department = p.Department,
        EmploymentType = p.EmploymentType,
        Seniority = p.Seniority,
        Openings = p.Openings,
        WorkArrangement = p.WorkArrangement,
        Location = p.Location,
        Country = p.Country,
        SalaryMin = p.SalaryMin,
        SalaryMax = p.SalaryMax,
        SalaryCurrency = p.SalaryCurrency,
        PayPeriod = p.PayPeriod,
        SalaryVisible = p.SalaryVisible,
        Description = p.Description,
        Responsibilities = p.Responsibilities,
        Requirements = p.Requirements,
        Skills = p.Skills,
        ApplicationUrl = p.ApplicationUrl,
        ApplicationEmail = p.ApplicationEmail,
        ClosingDate = p.ClosingDate,
        Status = p.Status == "Draft" ? "Draft" : "Published",
    };

    public void Dispose() => _factory.Dispose();
}
