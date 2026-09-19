using TalentBridge.Post.Domain.JobPostings;

namespace TalentBridge.Post.Api.JobPostings;

/// <summary>
/// Everything a manager supplies. Properties are nullable on purpose: a missing
/// JSON field must reach the validator (and come back as a field-level error),
/// not blow up deserialization.
/// </summary>
public record CreateJobPostingRequest
{
    public string? ReferenceCode { get; init; }
    public string? Title { get; init; }
    public string? Department { get; init; }
    public string? EmploymentType { get; init; }
    public string? Seniority { get; init; }
    public int? Openings { get; init; } = 1;
    public string? WorkArrangement { get; init; }
    public string? Location { get; init; }
    public string? Country { get; init; }
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }
    public string? SalaryCurrency { get; init; }
    public string? PayPeriod { get; init; } = "Annual";
    public bool SalaryVisible { get; init; } = true;
    public string? Description { get; init; }
    public string? Responsibilities { get; init; }
    public string? Requirements { get; init; }
    public string?[]? Skills { get; init; }
    public string? ApplicationUrl { get; init; }
    public string? ApplicationEmail { get; init; }
    public DateOnly? ClosingDate { get; init; }

    /// <summary>"Published" (default) or "Draft". A draft is never sent to the public board.</summary>
    public string? Status { get; init; } = JobPostingStatus.Published;

    /// <summary>Converts a request that has already passed validation. Never call it on an unvalidated one.</summary>
    public JobPostingContent ToContent()
    {
        var location = string.IsNullOrWhiteSpace(Location) ? Country! : Location;
        var skills = (Skills ?? [])
            .Select(s => s?.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new JobPostingContent(
            ReferenceCode: NullIfBlank(ReferenceCode),
            Title: Title!.Trim(),
            Department: Department!.Trim(),
            EmploymentType: EmploymentType!,
            Seniority: Seniority!,
            Openings: Openings!.Value,
            WorkArrangement: WorkArrangement!,
            Location: location.Trim(),
            Country: Country!.Trim(),
            SalaryMin: SalaryMin!.Value,
            SalaryMax: SalaryMax!.Value,
            SalaryCurrency: SalaryCurrency!.ToUpperInvariant(),
            PayPeriod: PayPeriod!,
            SalaryVisible: SalaryVisible,
            Description: Description!.Trim(),
            Responsibilities: NullIfBlank(Responsibilities),
            Requirements: NullIfBlank(Requirements),
            Skills: skills!,
            ApplicationUrl: NullIfBlank(ApplicationUrl),
            ApplicationEmail: NullIfBlank(ApplicationEmail),
            ClosingDate: ClosingDate!.Value);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>PUT body: the same content plus the version the client loaded, for optimistic concurrency.</summary>
public sealed record UpdateJobPostingRequest : CreateJobPostingRequest
{
    public int? Version { get; init; }
}

public sealed record JobPostingListQuery(
    string? Q,
    string? Status,
    string? Sort,
    int Page = 1,
    int PageSize = 20);

/// <summary>The complete persisted record. Confirmation and edit screens render exactly this.</summary>
public sealed record JobPostingResponse(
    Guid Id,
    string Slug,
    string Status,
    bool IsOpen,
    string? ReferenceCode,
    string Title,
    string Department,
    string EmploymentType,
    string Seniority,
    int Openings,
    string WorkArrangement,
    string Location,
    string Country,
    decimal SalaryMin,
    decimal SalaryMax,
    string SalaryCurrency,
    string PayPeriod,
    bool SalaryVisible,
    string Description,
    string? Responsibilities,
    string? Requirements,
    string[] Skills,
    string? ApplicationUrl,
    string? ApplicationEmail,
    DateOnly ClosingDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    int Version)
{
    public static JobPostingResponse From(JobPosting p, DateOnly today) => new(
        p.Id, p.Slug, p.EffectiveStatusOn(today), p.IsOpenOn(today), p.ReferenceCode, p.Title, p.Department,
        p.EmploymentType, p.Seniority, p.Openings, p.WorkArrangement, p.Location, p.Country,
        p.SalaryMin, p.SalaryMax, p.SalaryCurrency, p.PayPeriod, p.SalaryVisible,
        p.Description, p.Responsibilities, p.Requirements, p.Skills, p.ApplicationUrl, p.ApplicationEmail,
        p.ClosingDate, p.CreatedAt, p.UpdatedAt, p.PublishedAt, p.Version);
}

/// <summary>Dashboard row (wireframe 1.3). Deliberately smaller than the full record.</summary>
public sealed record JobPostingSummary(
    Guid Id,
    string Slug,
    string Status,
    string? ReferenceCode,
    string Title,
    string Department,
    string Location,
    string WorkArrangement,
    DateOnly ClosingDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static JobPostingSummary From(JobPosting p, DateOnly today) => new(
        p.Id, p.Slug, p.EffectiveStatusOn(today), p.ReferenceCode, p.Title, p.Department,
        p.Location, p.WorkArrangement, p.ClosingDate, p.CreatedAt, p.UpdatedAt);
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total, int TotalPages);
