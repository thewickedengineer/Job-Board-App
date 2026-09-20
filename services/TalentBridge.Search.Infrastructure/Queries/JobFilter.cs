using Dapper;

namespace TalentBridge.Search.Infrastructure.Queries;

/// <summary>
/// The candidate's filter state, already validated by the API layer. Shared by
/// the list and facet queries so both see exactly the same rows.
/// </summary>
public sealed record JobFilter(
    string? Q,
    string[] Departments,
    string? Location,
    string[] WorkArrangements,
    string[] EmploymentTypes,
    string[] Seniorities,
    decimal? SalaryMin,
    decimal? SalaryMax,
    int? PostedWithinDays)
{
    public static readonly JobFilter None = new(null, [], null, [], [], [], null, null, null);

    public bool HasKeyword => !string.IsNullOrWhiteSpace(Q);

    /// <summary>The facet dimensions; a facet's own filter is left out when counting it.</summary>
    public enum Dimension
    {
        None,
        Department,
        WorkArrangement,
        EmploymentType,
        Seniority,
    }

    /// <summary>
    /// Builds the WHERE clause from a fixed set of predicate fragments. Every value
    /// travels as a parameter — user input never touches the SQL text.
    /// </summary>
    internal (string Where, DynamicParameters Parameters) ToSql(Dimension exclude = Dimension.None)
    {
        var predicates = new List<string>
        {
            // The board only ever shows open, unexpired postings. Expiry is
            // evaluated here because current_date cannot live in a generated column.
            "is_open",
            "closing_date >= current_date",
        };
        var p = new DynamicParameters();

        if (HasKeyword)
        {
            predicates.Add("search_vector @@ websearch_to_tsquery('english', @q)");
            p.Add("q", Q!.Trim());
        }

        if (exclude != Dimension.Department && Departments.Length > 0)
        {
            predicates.Add("department = any(@departments)");
            p.Add("departments", Departments);
        }

        if (exclude != Dimension.WorkArrangement && WorkArrangements.Length > 0)
        {
            predicates.Add("work_arrangement = any(@workArrangements)");
            p.Add("workArrangements", WorkArrangements);
        }

        if (exclude != Dimension.EmploymentType && EmploymentTypes.Length > 0)
        {
            predicates.Add("employment_type = any(@employmentTypes)");
            p.Add("employmentTypes", EmploymentTypes);
        }

        if (exclude != Dimension.Seniority && Seniorities.Length > 0)
        {
            predicates.Add("seniority = any(@seniorities)");
            p.Add("seniorities", Seniorities);
        }

        if (!string.IsNullOrWhiteSpace(Location))
        {
            // Free-text "city or region". A short column checked after the indexed
            // predicates have narrowed the set; see ARCHITECTURE.md.
            predicates.Add("(location ilike @location or country ilike @location)");
            p.Add("location", $"%{EscapeLike(Location.Trim())}%");
        }

        if (SalaryMin is not null || SalaryMax is not null)
        {
            // Hidden salaries never match a salary filter, so the numbers cannot
            // be inferred by probing.
            predicates.Add("salary_visible");
            if (SalaryMin is not null)
            {
                predicates.Add("salary_max >= @salaryMin");
                p.Add("salaryMin", SalaryMin);
            }

            if (SalaryMax is not null)
            {
                predicates.Add("salary_min <= @salaryMax");
                p.Add("salaryMax", SalaryMax);
            }
        }

        if (PostedWithinDays is not null)
        {
            predicates.Add("published_at >= now() - make_interval(days => @postedWithinDays)");
            p.Add("postedWithinDays", PostedWithinDays);
        }

        return (string.Join(" and ", predicates), p);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
