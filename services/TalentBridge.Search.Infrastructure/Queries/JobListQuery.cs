using Dapper;
using Npgsql;

namespace TalentBridge.Search.Infrastructure.Queries;

/// <summary>Screen 2.1 — the results page. One round trip returns the page and the total.</summary>
public sealed class JobListQuery(NpgsqlDataSource dataSource)
{
    public const int MaxPageSize = 50;
    public const int ExcerptLength = 240;

    public static readonly IReadOnlyList<string> Sorts = ["recent", "relevance", "salaryDesc", "salaryAsc", "closingSoon"];

    // Every ordering ends with id so paging is deterministic (keyset-friendly).
    private static readonly Dictionary<string, string> OrderBy = new(StringComparer.Ordinal)
    {
        ["recent"] = "published_at desc, id desc",
        ["relevance"] = "ts_rank_cd(search_vector, websearch_to_tsquery('english', @q)) desc, published_at desc, id desc",
        ["salaryDesc"] = "salary_visible desc, salary_max desc, id desc",
        ["salaryAsc"] = "salary_visible desc, salary_min asc, id desc",
        ["closingSoon"] = "closing_date asc, published_at desc, id desc",
    };

    public async Task<JobPage> RunAsync(JobFilter filter, string sort, int page, int pageSize, CancellationToken ct)
    {
        if (!OrderBy.TryGetValue(sort, out var orderBy))
        {
            throw new ArgumentOutOfRangeException(nameof(sort), sort, "Unknown sort.");
        }

        // Relevance without a keyword has nothing to rank; fall back to recency.
        if (sort == "relevance" && !filter.HasKeyword)
        {
            orderBy = OrderBy["recent"];
        }

        var (where, parameters) = filter.ToSql();
        parameters.Add("limit", pageSize);
        parameters.Add("offset", (page - 1) * pageSize);

        var sql = $"""
            select id, slug, title, department, location, country, work_arrangement, employment_type, seniority,
                   salary_min, salary_max, salary_currency, pay_period, salary_visible,
                   left(description, {ExcerptLength}) as excerpt,
                   organization, closing_date, published_at,
                   count(*) over () as total
            from search.job_listings
            where {where}
            order by {orderBy}
            limit @limit offset @offset
            """;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = (await connection.QueryAsync<JobSummaryRow>(new CommandDefinition(sql, parameters, cancellationToken: ct))).ToList();
        var total = rows.Count > 0 ? (int)rows[0].Total : await CountAsync(connection, where, parameters, ct);
        return new JobPage(rows, total);
    }

    // An empty page past the end still needs the true total for totalPages.
    private static Task<int> CountAsync(NpgsqlConnection connection, string where, DynamicParameters parameters, CancellationToken ct) =>
        connection.ExecuteScalarAsync<int>(new CommandDefinition($"select count(*) from search.job_listings where {where}", parameters, cancellationToken: ct));
}

public sealed record JobPage(IReadOnlyList<JobSummaryRow> Items, int Total);

/// <summary>Dapper maps snake_case columns onto these by name (MatchNamesWithUnderscores).</summary>
public sealed record JobSummaryRow
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
    public string Excerpt { get; init; } = "";
    public string Organization { get; init; } = "";
    public DateOnly ClosingDate { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public long Total { get; init; }
}
