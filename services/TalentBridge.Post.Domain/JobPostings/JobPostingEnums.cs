namespace TalentBridge.Post.Domain.JobPostings;

/// <summary>
/// Closed vocabularies for the job posting. Stored as text in the database and
/// enforced by CHECK constraints (see <c>JobPostingConfiguration</c>) so that a
/// row cannot exist with an unknown value regardless of how it was written.
/// </summary>
public static class JobPostingVocabulary
{
    public static readonly IReadOnlyList<string> EmploymentTypes =
        ["FullTime", "PartTime", "Contract", "Temporary", "Internship"];

    public static readonly IReadOnlyList<string> Seniorities =
        ["Intern", "Junior", "Mid", "Senior", "Lead", "Principal", "Director", "Executive"];

    public static readonly IReadOnlyList<string> WorkArrangements =
        ["OnSite", "Hybrid", "Remote"];

    public static readonly IReadOnlyList<string> PayPeriods =
        ["Annual", "Monthly", "Hourly"];

    public static readonly IReadOnlyList<string> Currencies =
        ["CAD", "USD", "GBP", "EUR", "AUD", "INR"];

    public static readonly IReadOnlyList<string> Statuses =
        [JobPostingStatus.Draft, JobPostingStatus.Published, JobPostingStatus.Closed];
}

public static class JobPostingStatus
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Closed = "Closed";
}
