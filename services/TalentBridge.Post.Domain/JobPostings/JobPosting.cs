using TalentBridge.Post.Domain.Managers;

namespace TalentBridge.Post.Domain.JobPostings;

/// <summary>
/// Write-model aggregate for a job posting. All mutation goes through methods on
/// this type so that <see cref="Version"/> and <see cref="UpdatedAt"/> are always
/// maintained and the outbox sees a consistent snapshot.
/// </summary>
public sealed class JobPosting
{
    public const int MaxSkills = 20;

    public Guid Id { get; private set; }
    public Guid ManagerId { get; private set; }
    public Manager? Manager { get; private set; }

    public string? ReferenceCode { get; private set; }
    public string Title { get; private set; } = null!;
    public string Department { get; private set; } = null!;
    public string EmploymentType { get; private set; } = null!;
    public string Seniority { get; private set; } = null!;
    public int Openings { get; private set; }
    public string WorkArrangement { get; private set; } = null!;
    public string Location { get; private set; } = null!;
    public string Country { get; private set; } = null!;

    public decimal SalaryMin { get; private set; }
    public decimal SalaryMax { get; private set; }
    public string SalaryCurrency { get; private set; } = null!;
    public string PayPeriod { get; private set; } = null!;
    public bool SalaryVisible { get; private set; }

    public string Description { get; private set; } = null!;
    public string? Responsibilities { get; private set; }
    public string? Requirements { get; private set; }
    public string[] Skills { get; private set; } = [];

    public string? ApplicationUrl { get; private set; }
    public string? ApplicationEmail { get; private set; }
    public DateOnly ClosingDate { get; private set; }

    public string Status { get; private set; } = null!;
    public string Slug { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>
    /// Monotonic per-row version. Incremented on every mutation, used as the EF
    /// concurrency token and carried on every outbox message so the read side can
    /// discard stale projections.
    /// </summary>
    public int Version { get; private set; }

    private JobPosting() { }

    public static JobPosting Create(Guid managerId, JobPostingContent content, string slug, bool publish, DateTimeOffset now)
    {
        var posting = new JobPosting
        {
            Id = Guid.CreateVersion7(),
            ManagerId = managerId,
            Slug = slug,
            Status = JobPostingStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        posting.Apply(content);
        if (publish)
        {
            posting.Status = JobPostingStatus.Published;
            posting.PublishedAt = now;
        }

        return posting;
    }

    public bool IsDraft => Status == JobPostingStatus.Draft;
    public bool IsPublished => Status == JobPostingStatus.Published;
    public bool IsClosed => Status == JobPostingStatus.Closed;

    /// <summary>
    /// Replace the editable content and, optionally, promote a draft to
    /// published. A published posting stays published (it cannot revert to
    /// draft) and a closed posting cannot be edited at all.
    /// </summary>
    public void Update(JobPostingContent content, bool publish, DateTimeOffset now)
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("A closed posting cannot be edited.");
        }

        Apply(content);
        if (publish && IsDraft)
        {
            Status = JobPostingStatus.Published;
            PublishedAt = now;
        }

        Touch(now);
    }

    /// <summary>Irreversible. Returns false when the posting was already closed.</summary>
    public bool Close(DateTimeOffset now)
    {
        if (IsClosed)
        {
            return false;
        }

        Status = JobPostingStatus.Closed;
        Touch(now);
        return true;
    }

    public bool IsOpenOn(DateOnly today) => IsPublished && ClosingDate >= today;

    /// <summary>The dashboard's notion of status: Published postings past their closing date show as Expired.</summary>
    public string EffectiveStatusOn(DateOnly today) =>
        IsPublished && ClosingDate < today ? JobPostingStatus.Expired : Status;

    private void Apply(JobPostingContent c)
    {
        ReferenceCode = c.ReferenceCode;
        Title = c.Title;
        Department = c.Department;
        EmploymentType = c.EmploymentType;
        Seniority = c.Seniority;
        Openings = c.Openings;
        WorkArrangement = c.WorkArrangement;
        Location = c.Location;
        Country = c.Country;
        SalaryMin = c.SalaryMin;
        SalaryMax = c.SalaryMax;
        SalaryCurrency = c.SalaryCurrency;
        PayPeriod = c.PayPeriod;
        SalaryVisible = c.SalaryVisible;
        Description = c.Description;
        Responsibilities = c.Responsibilities;
        Requirements = c.Requirements;
        Skills = c.Skills;
        ApplicationUrl = c.ApplicationUrl;
        ApplicationEmail = c.ApplicationEmail;
        ClosingDate = c.ClosingDate;
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

/// <summary>
/// The manager-editable fields of a posting, already validated. Everything not
/// here (id, slug, status, timestamps, version) is owned by the server.
/// </summary>
public sealed record JobPostingContent(
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
    DateOnly ClosingDate);
