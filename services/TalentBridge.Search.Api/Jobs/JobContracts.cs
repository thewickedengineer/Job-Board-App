using TalentBridge.Search.Infrastructure.Queries;

namespace TalentBridge.Search.Api.Jobs;

/// <summary>GET /api/jobs query string. Repeated keys (department=A&department=B) bind to arrays.</summary>
public sealed record JobListRequest(
    string? Q,
    string[]? Department,
    string? Location,
    string[]? WorkArrangement,
    string[]? EmploymentType,
    string[]? Seniority,
    decimal? SalaryMin,
    decimal? SalaryMax,
    int? PostedWithinDays,
    string? Sort,
    int Page = 1,
    int PageSize = 20)
{
    public JobFilter ToFilter() => new(
        Q,
        Clean(Department),
        Location,
        Clean(WorkArrangement),
        Clean(EmploymentType),
        Clean(Seniority),
        SalaryMin,
        SalaryMax,
        PostedWithinDays);

    private static string[] Clean(string[]? values) =>
        (values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total, int TotalPages);

/// <summary>A results card. Salary numbers are withheld (null) when the manager hid them.</summary>
public sealed record JobSummaryResponse(
    Guid Id,
    string Slug,
    string Title,
    string Department,
    string Location,
    string Country,
    string WorkArrangement,
    string EmploymentType,
    string Seniority,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string SalaryCurrency,
    string PayPeriod,
    bool SalaryVisible,
    string Excerpt,
    string Organization,
    DateOnly ClosingDate,
    DateTimeOffset PublishedAt)
{
    public static JobSummaryResponse From(JobSummaryRow r) => new(
        r.Id, r.Slug, r.Title, r.Department, r.Location, r.Country, r.WorkArrangement, r.EmploymentType, r.Seniority,
        r.SalaryVisible ? r.SalaryMin : null, r.SalaryVisible ? r.SalaryMax : null, r.SalaryCurrency, r.PayPeriod, r.SalaryVisible,
        r.Excerpt, r.Organization, r.ClosingDate, r.PublishedAt);
}

public sealed record JobDetailResponse(
    Guid Id,
    string Slug,
    string Title,
    string Department,
    string Location,
    string Country,
    string WorkArrangement,
    string EmploymentType,
    string Seniority,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string SalaryCurrency,
    string PayPeriod,
    bool SalaryVisible,
    string Description,
    string? Responsibilities,
    string? Requirements,
    string[] Skills,
    string Organization,
    string? ApplicationUrl,
    string? ApplicationEmail,
    DateOnly ClosingDate,
    DateTimeOffset PublishedAt,
    bool IsOpen,
    IReadOnlyList<SimilarJobResponse> Similar)
{
    public static JobDetailResponse From(JobDetail d)
    {
        var j = d.Job;
        return new(
            j.Id, j.Slug, j.Title, j.Department, j.Location, j.Country, j.WorkArrangement, j.EmploymentType, j.Seniority,
            j.SalaryVisible ? j.SalaryMin : null, j.SalaryVisible ? j.SalaryMax : null, j.SalaryCurrency, j.PayPeriod, j.SalaryVisible,
            j.Description, j.Responsibilities, j.Requirements, j.Skills, j.Organization, j.ApplicationUrl, j.ApplicationEmail,
            j.ClosingDate, j.PublishedAt, j.IsOpen,
            d.Similar.Select(SimilarJobResponse.From).ToList());
    }
}

public sealed record SimilarJobResponse(
    string Slug,
    string Title,
    string Department,
    string Location,
    string WorkArrangement,
    string EmploymentType,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string SalaryCurrency,
    string PayPeriod,
    bool SalaryVisible)
{
    public static SimilarJobResponse From(SimilarJobRow r) => new(
        r.Slug, r.Title, r.Department, r.Location, r.WorkArrangement, r.EmploymentType,
        r.SalaryVisible ? r.SalaryMin : null, r.SalaryVisible ? r.SalaryMax : null, r.SalaryCurrency, r.PayPeriod, r.SalaryVisible);
}

public sealed record FacetsResponse(
    IReadOnlyList<FacetValue> Departments,
    IReadOnlyList<FacetValue> Locations,
    IReadOnlyList<FacetValue> WorkArrangements,
    IReadOnlyList<FacetValue> EmploymentTypes,
    IReadOnlyList<FacetValue> Seniorities,
    int Total)
{
    public static FacetsResponse From(Facets f) =>
        new(f.Departments, f.Locations, f.WorkArrangements, f.EmploymentTypes, f.Seniorities, f.Total);
}
