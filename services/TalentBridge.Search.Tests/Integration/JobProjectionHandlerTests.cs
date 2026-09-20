using Dapper;
using TalentBridge.Search.Infrastructure.Projections;

namespace TalentBridge.Search.Tests.Integration;

[Collection(SearchPostgresCollection.Name)]
public sealed class JobProjectionHandlerTests(SearchPostgresFixture postgres)
{
    private readonly JobProjectionHandler _handler = new(postgres.DataSource);

    public static JobProjectionMessage Message(Guid id, int version, string? title = null, bool isOpen = true) => new(
        Id: id,
        Version: version,
        Slug: $"senior-warehouse-supervisor-{id:N}",
        Title: title ?? "Senior Warehouse Supervisor",
        Department: "Operations",
        Location: "Leeds, West Yorkshire",
        Country: "United Kingdom",
        WorkArrangement: "Hybrid",
        EmploymentType: "FullTime",
        Seniority: "Senior",
        SalaryMin: 38_000,
        SalaryMax: 46_000,
        SalaryCurrency: "GBP",
        PayPeriod: "Annual",
        SalaryVisible: true,
        Description: "Northline's Leeds distribution hub handles 40,000 outbound units a week.",
        Responsibilities: "Run the evening shift",
        Requirements: null,
        Skills: ["Warehouse ops", "WMS"],
        Organization: "Northline",
        ApplicationUrl: null,
        ApplicationEmail: "careers@northline.co",
        ClosingDate: new DateOnly(2026, 10, 23),
        PublishedAt: new DateTimeOffset(2026, 9, 19, 14, 31, 0, TimeSpan.Zero),
        IsOpen: isOpen);

    private async Task<(int Version, string Title, bool IsOpen, DateTime ProjectedAt, string[] Skills)> RowAsync(Guid id)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<(int, string, bool, DateTime, string[])>(
            "select version, title, is_open, projected_at, skills from search.job_listings where id = @id", new { id });
    }

    [Fact]
    public async Task First_apply_inserts_the_full_row()
    {
        var id = Guid.NewGuid();

        var applied = await _handler.ApplyAsync(Message(id, 1));

        Assert.True(applied);
        var row = await RowAsync(id);
        Assert.Equal(1, row.Version);
        Assert.Equal("Senior Warehouse Supervisor", row.Title);
        Assert.True(row.IsOpen);
        Assert.Equal(["Warehouse ops", "WMS"], row.Skills);

        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        var hit = await connection.ExecuteScalarAsync<int>(
            "select count(*) from search.job_listings where id = @id and search_vector @@ websearch_to_tsquery('english', 'warehouse')", new { id });
        Assert.Equal(1, hit);
    }

    [Fact]
    public async Task Applying_the_same_message_twice_is_a_no_op()
    {
        var id = Guid.NewGuid();
        Assert.True(await _handler.ApplyAsync(Message(id, 1)));
        var first = await RowAsync(id);

        var applied = await _handler.ApplyAsync(Message(id, 1, title: "Tampered replay"));

        Assert.False(applied);
        var second = await RowAsync(id);
        // Title, version and projected_at all untouched (arrays compared by value).
        Assert.Equal((first.Version, first.Title, first.IsOpen, first.ProjectedAt), (second.Version, second.Title, second.IsOpen, second.ProjectedAt));
        Assert.Equal(first.Skills, second.Skills);
    }

    [Fact]
    public async Task An_older_version_is_ignored_and_a_newer_one_replaces_the_row()
    {
        var id = Guid.NewGuid();
        Assert.True(await _handler.ApplyAsync(Message(id, 2, title: "Version two")));

        var stale = await _handler.ApplyAsync(Message(id, 1, title: "Version one, delivered late"));
        Assert.False(stale);
        Assert.Equal("Version two", (await RowAsync(id)).Title);

        var newer = await _handler.ApplyAsync(Message(id, 3, title: "Version three", isOpen: false));
        Assert.True(newer);
        var row = await RowAsync(id);
        Assert.Equal((3, "Version three", false), (row.Version, row.Title, row.IsOpen));
    }
}
