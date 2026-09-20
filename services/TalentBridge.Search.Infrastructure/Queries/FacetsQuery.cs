using Dapper;
using Npgsql;

namespace TalentBridge.Search.Infrastructure.Queries;

/// <summary>
/// The filter rail's counts (screens 2.1/2.2). Each dimension is counted with
/// every other filter applied but its own left out, so a candidate can see what
/// adding a second department would return. Five grouped queries, one round trip.
/// </summary>
public sealed class FacetsQuery(NpgsqlDataSource dataSource)
{
    public const int MaxValuesPerFacet = 25;

    public async Task<Facets> RunAsync(JobFilter filter, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        var batch = string.Join(";\n", new[]
        {
            Facet("department", filter, JobFilter.Dimension.Department, parameters),
            Facet("location", filter, JobFilter.Dimension.None, parameters),
            Facet("work_arrangement", filter, JobFilter.Dimension.WorkArrangement, parameters),
            Facet("employment_type", filter, JobFilter.Dimension.EmploymentType, parameters),
            Facet("seniority", filter, JobFilter.Dimension.Seniority, parameters),
            Total(filter, parameters),
        });

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var grid = await connection.QueryMultipleAsync(new CommandDefinition(batch, parameters, cancellationToken: ct));

        var departments = (await grid.ReadAsync<FacetValue>()).ToList();
        var locations = (await grid.ReadAsync<FacetValue>()).ToList();
        var workArrangements = (await grid.ReadAsync<FacetValue>()).ToList();
        var employmentTypes = (await grid.ReadAsync<FacetValue>()).ToList();
        var seniorities = (await grid.ReadAsync<FacetValue>()).ToList();
        var total = await grid.ReadSingleAsync<int>();

        return new Facets(departments, locations, workArrangements, employmentTypes, seniorities, total);
    }

    // Parameter names are suffixed per statement because each statement builds
    // its own WHERE (a different dimension excluded) inside one batch.
    private static string Facet(string column, JobFilter filter, JobFilter.Dimension exclude, DynamicParameters into)
    {
        var (where, parameters) = filter.ToSql(exclude);
        var suffix = $"_{column}";
        foreach (var name in parameters.ParameterNames)
        {
            into.Add(name + suffix, parameters.Get<object?>(name));
        }

        var whereSuffixed = SuffixParameters(where, parameters.ParameterNames, suffix);
        return $"""
            select {column} as value, count(*)::int as count
            from search.job_listings
            where {whereSuffixed}
            group by {column}
            order by count desc, value asc
            limit {MaxValuesPerFacet}
            """;
    }

    private static string Total(JobFilter filter, DynamicParameters into)
    {
        var (where, parameters) = filter.ToSql();
        foreach (var name in parameters.ParameterNames)
        {
            into.Add(name + "_total", parameters.Get<object?>(name));
        }

        return $"select count(*)::int from search.job_listings where {SuffixParameters(where, parameters.ParameterNames, "_total")}";
    }

    private static string SuffixParameters(string sql, IEnumerable<string> names, string suffix)
    {
        // Longest names first so @salaryMin is not rewritten inside @salaryMinX.
        foreach (var name in names.OrderByDescending(n => n.Length))
        {
            sql = sql.Replace("@" + name, "@" + name + suffix, StringComparison.Ordinal);
        }

        return sql;
    }
}

public sealed record Facets(
    IReadOnlyList<FacetValue> Departments,
    IReadOnlyList<FacetValue> Locations,
    IReadOnlyList<FacetValue> WorkArrangements,
    IReadOnlyList<FacetValue> EmploymentTypes,
    IReadOnlyList<FacetValue> Seniorities,
    int Total);

public sealed record FacetValue(string Value, int Count);
