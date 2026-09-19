using TalentBridge.Post.Domain.JobPostings;

namespace TalentBridge.Post.Domain.Outbox;

/// <summary>
/// The payload pushed to the Search API's projection endpoint: a complete
/// snapshot of everything <c>search.job_listings</c> stores, so applying it is a
/// single idempotent upsert keyed on <see cref="Id"/> and guarded by
/// <see cref="Version"/>. The Search API keeps its own copy of this shape —
/// the two services share a wire contract, not code.
/// </summary>
public sealed record JobProjectionMessage(
    Guid Id,
    int Version,
    string Slug,
    string Title,
    string Department,
    string Location,
    string Country,
    string WorkArrangement,
    string EmploymentType,
    string Seniority,
    decimal SalaryMin,
    decimal SalaryMax,
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
    bool IsOpen)
{
    public const string MessageType = "job-posting.changed";

    /// <remarks>Only meaningful for a posting that has been published at some point.</remarks>
    public static JobProjectionMessage From(JobPosting p, string organization) =>
        new(
            p.Id,
            p.Version,
            p.Slug,
            p.Title,
            p.Department,
            p.Location,
            p.Country,
            p.WorkArrangement,
            p.EmploymentType,
            p.Seniority,
            p.SalaryMin,
            p.SalaryMax,
            p.SalaryCurrency,
            p.PayPeriod,
            p.SalaryVisible,
            p.Description,
            p.Responsibilities,
            p.Requirements,
            p.Skills,
            organization,
            p.ApplicationUrl,
            p.ApplicationEmail,
            p.ClosingDate,
            p.PublishedAt ?? throw new InvalidOperationException("Only published postings are projected."),
            IsOpen: p.IsPublished);
}
