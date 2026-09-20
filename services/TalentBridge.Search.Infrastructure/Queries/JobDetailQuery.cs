using Dapper;
using Npgsql;

namespace TalentBridge.Search.Infrastructure.Queries;

/// <summary>
/// Screen 2.3 — one posting by slug plus a short list of similar open roles.
/// Closed and expired postings are still returned (the page renders a closed
/// banner); only slugs that never existed are a miss.
/// </summary>
public sealed class JobDetailQuery(NpgsqlDataSource dataSource)
{
    public const int SimilarCount = 3;

    private const string DetailSql = """
        select id, slug, title, department, location, country, work_arrangement, employment_type, seniority,
               salary_min, salary_max, salary_currency, pay_period, salary_visible,
               description, responsibilities, requirements, skills, organization,
               application_url, application_email, closing_date, published_at,
               (is_open and closing_date >= current_date) as is_open,
               version, projected_at
        from search.job_listings
        where slug = @slug
        """;

    // Same department first, then anything else open; newest first.
    private const string SimilarSql = """
        select id, slug, title, department, location, work_arrangement, employment_type,
               salary_min, salary_max, salary_currency, pay_period, salary_visible, version
        from search.job_listings
        where id <> @id and is_open and closing_date >= current_date
        order by (department = @department) desc, published_at desc, id desc
        limit @limit
        """;

    public async Task<JobDetail?> RunAsync(string slug, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<JobDetailRow>(new CommandDefinition(DetailSql, new { slug }, cancellationToken: ct));
        if (row is null)
        {
            return null;
        }

        var similar = await connection.QueryAsync<SimilarJobRow>(new CommandDefinition(
            SimilarSql, new { row.Id, row.Department, limit = SimilarCount }, cancellationToken: ct));
        return new JobDetail(row, similar.ToList());
    }
}

public sealed record JobDetail(JobDetailRow Job, IReadOnlyList<SimilarJobRow> Similar);

public sealed record JobDetailRow
{
    public Guid Id { get; init; }
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Department { get; init; } = "";
    public string Location { get; init; } = "";
    public string Country { get; init; } = "";
    public string WorkArrangement { get; init; } = "";
    public string EmploymentType { get; init; } = "";
    public string Seniority { get; init; } = "";
    public decimal SalaryMin { get; init; }
    public decimal SalaryMax { get; init; }
    public string SalaryCurrency { get; init; } = "";
    public string PayPeriod { get; init; } = "";
    public bool SalaryVisible { get; init; }
    public string Description { get; init; } = "";
    public string? Responsibilities { get; init; }
    public string? Requirements { get; init; }
    public string[] Skills { get; init; } = [];
    public string Organization { get; init; } = "";
    public string? ApplicationUrl { get; init; }
    public string? ApplicationEmail { get; init; }
    public DateOnly ClosingDate { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public bool IsOpen { get; init; }
    public int Version { get; init; }
    public DateTimeOffset ProjectedAt { get; init; }
}

public sealed record SimilarJobRow
{
    public Guid Id { get; init; }
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Department { get; init; } = "";
    public string Location { get; init; } = "";
    public string WorkArrangement { get; init; } = "";
    public string EmploymentType { get; init; } = "";
    public decimal SalaryMin { get; init; }
    public decimal SalaryMax { get; init; }
    public string SalaryCurrency { get; init; } = "";
    public string PayPeriod { get; init; } = "";
    public bool SalaryVisible { get; init; }
    public int Version { get; init; }
}
