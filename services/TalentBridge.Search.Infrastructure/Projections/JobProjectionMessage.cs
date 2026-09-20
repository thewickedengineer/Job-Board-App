namespace TalentBridge.Search.Infrastructure.Projections;

/// <summary>
/// The Search side's copy of the wire contract the Post API's outbox sends. The
/// two services share the shape, not code — CLAUDE.md keeps them independently
/// deployable. Property names must match the JSON the Post API emits (camelCase).
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
    bool IsOpen);
