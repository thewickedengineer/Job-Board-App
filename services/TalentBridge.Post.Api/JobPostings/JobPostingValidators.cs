using FluentValidation;
using TalentBridge.Post.Api.Validation;
using TalentBridge.Post.Domain.JobPostings;

namespace TalentBridge.Post.Api.JobPostings;

/// <summary>
/// The authoritative rules from CLAUDE.md §6. The Angular client mirrors them
/// for latency only. Property names become the camelCase keys the client maps
/// onto its form controls, so cross-field failures are attached to the control
/// the user should fix (salary range → salaryMax).
/// </summary>
public class CreateJobPostingValidator : AbstractValidator<CreateJobPostingRequest>
{
    public const decimal MaxSalary = 10_000_000m;

    public CreateJobPostingValidator(TimeProvider clock)
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Job title is required.")
            .Length(3, 120).WithMessage("Job title must be 3–120 characters.");

        RuleFor(x => x.Department)
            .NotEmpty().WithMessage("Department is required.")
            .Length(2, 80).WithMessage("Department must be 2–80 characters.");

        RuleFor(x => x.EmploymentType)
            .NotEmpty().WithMessage("Employment type is required.")
            .Must(BeOneOf(JobPostingVocabulary.EmploymentTypes)).WithMessage(OneOfMessage("Employment type", JobPostingVocabulary.EmploymentTypes));

        RuleFor(x => x.Seniority)
            .NotEmpty().WithMessage("Seniority is required.")
            .Must(BeOneOf(JobPostingVocabulary.Seniorities)).WithMessage(OneOfMessage("Seniority", JobPostingVocabulary.Seniorities));

        RuleFor(x => x.Openings)
            .NotNull().WithMessage("Number of openings is required.")
            .InclusiveBetween(1, 999).WithMessage("Number of openings must be a whole number from 1 to 999.");

        RuleFor(x => x.WorkArrangement)
            .NotEmpty().WithMessage("Work arrangement is required.")
            .Must(BeOneOf(JobPostingVocabulary.WorkArrangements)).WithMessage(OneOfMessage("Work arrangement", JobPostingVocabulary.WorkArrangements));

        // Location is required unless the role is Remote, where the country stands in.
        RuleFor(x => x.Location)
            .NotEmpty().WithMessage("Location is required unless the work arrangement is Remote.")
            .When(x => x.WorkArrangement != "Remote");
        RuleFor(x => x.Location)
            .Length(2, 120).WithMessage("Location must be 2–120 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Location));

        RuleFor(x => x.Country)
            .NotEmpty().WithMessage("Country is required.")
            .Length(2, 80).WithMessage("Country must be 2–80 characters.");

        RuleFor(x => x.SalaryMin)
            .NotNull().WithMessage("Salary minimum is required.")
            .GreaterThan(0).WithMessage("Salary minimum must be greater than 0.")
            .LessThanOrEqualTo(MaxSalary).WithMessage("Salary minimum must be at most 10,000,000.");

        RuleFor(x => x.SalaryMax)
            .NotNull().WithMessage("Salary maximum is required.")
            .GreaterThan(0).WithMessage("Salary maximum must be greater than 0.")
            .LessThanOrEqualTo(MaxSalary).WithMessage("Salary maximum must be at most 10,000,000.");

        // Cross-field rule, keyed to salaryMax by contract (§6). Only evaluated
        // once both values are individually valid so the user sees one error.
        RuleFor(x => x.SalaryMax)
            .GreaterThan(x => x.SalaryMin).WithMessage("Salary maximum must be greater than salary minimum.")
            .When(x => x.SalaryMin is > 0 and <= MaxSalary && x.SalaryMax is > 0 and <= MaxSalary);

        RuleFor(x => x.SalaryCurrency)
            .NotEmpty().WithMessage("Currency is required.")
            .Must(c => c is not null && JobPostingVocabulary.Currencies.Contains(c.ToUpperInvariant()))
                .WithMessage(OneOfMessage("Currency", JobPostingVocabulary.Currencies));

        RuleFor(x => x.PayPeriod)
            .NotEmpty().WithMessage("Pay period is required.")
            .Must(BeOneOf(JobPostingVocabulary.PayPeriods)).WithMessage(OneOfMessage("Pay period", JobPostingVocabulary.PayPeriods));

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Job description is required.")
            .Length(50, 10_000).WithMessage("Job description must be 50–10,000 characters.");

        RuleFor(x => x.Responsibilities).MaximumLength(10_000).WithMessage("Responsibilities must be at most 10,000 characters.");
        RuleFor(x => x.Requirements).MaximumLength(10_000).WithMessage("Requirements must be at most 10,000 characters.");

        // The chip input is a single form control, so both rules key to "skills".
        RuleFor(x => x.Skills)
            .Must(s => s is null || s.Length <= JobPosting.MaxSkills).WithMessage("Add at most 20 skills.")
            .Must(s => s is null || s.All(e => e is not null && e.Trim().Length is >= 1 and <= 40))
                .WithMessage("Each skill must be 1–40 characters.")
                .When(x => x.Skills is { Length: <= JobPosting.MaxSkills }, ApplyConditionTo.CurrentValidator);

        RuleFor(x => x.ApplicationEmail)
            .MustBeEmailAddress().WithMessage("Enter a valid application email address.")
            .When(x => !string.IsNullOrWhiteSpace(x.ApplicationEmail));

        RuleFor(x => x.ApplicationUrl)
            .Must(BeAbsoluteHttpUrl).WithMessage("Application URL must be an absolute http:// or https:// link.")
            .MaximumLength(2048)
            .When(x => !string.IsNullOrWhiteSpace(x.ApplicationUrl));

        RuleFor(x => x.ReferenceCode)
            .MaximumLength(40).WithMessage("Internal reference code must be at most 40 characters.");

        RuleFor(x => x.ClosingDate)
            .NotNull().WithMessage("Closing date is required.")
            .Must(d => d > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                .WithMessage("Closing date must be in the future.");

        RuleFor(x => x.Status)
            .NotEmpty().WithMessage("Status is required.")
            .Must(s => s is JobPostingStatus.Draft or JobPostingStatus.Published)
                .WithMessage("Status must be Draft or Published.");
    }

    private static Func<string?, bool> BeOneOf(IReadOnlyList<string> allowed) =>
        value => value is not null && allowed.Contains(value, StringComparer.Ordinal);

    private static string OneOfMessage(string field, IReadOnlyList<string> allowed) =>
        $"{field} must be one of: {string.Join(", ", allowed)}.";

    private static bool BeAbsoluteHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host);
}

public sealed class UpdateJobPostingValidator : AbstractValidator<UpdateJobPostingRequest>
{
    public UpdateJobPostingValidator(TimeProvider clock)
    {
        Include(new CreateJobPostingValidator(clock));

        RuleFor(x => x.Version)
            .NotNull().WithMessage("Version is required — send the version you loaded.")
            .GreaterThanOrEqualTo(1).WithMessage("Version must be at least 1.");
    }
}
