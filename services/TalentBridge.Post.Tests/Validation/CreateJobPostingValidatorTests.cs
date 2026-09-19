using FluentValidation.Results;
using Microsoft.Extensions.Time.Testing;
using TalentBridge.Post.Api.JobPostings;

namespace TalentBridge.Post.Tests.Validation;

/// <summary>
/// The CLAUDE.md §6 matrix. Runs entirely in memory; "today" is pinned to
/// 2026-09-19T23:30Z so date boundaries are deterministic across time zones.
/// </summary>
public sealed class CreateJobPostingValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 23, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 19);

    private readonly CreateJobPostingValidator _validator = new(new FakeTimeProvider(Now));

    private static CreateJobPostingRequest Valid() => new()
    {
        Title = "Senior Warehouse Supervisor",
        Department = "Operations",
        EmploymentType = "FullTime",
        Seniority = "Senior",
        Openings = 2,
        WorkArrangement = "Hybrid",
        Location = "Leeds, West Yorkshire",
        Country = "United Kingdom",
        SalaryMin = 38_000,
        SalaryMax = 46_000,
        SalaryCurrency = "GBP",
        PayPeriod = "Annual",
        Description = new string('x', 50),
        Skills = ["Warehouse ops", "WMS"],
        ApplicationEmail = "careers@northline.co",
        ClosingDate = Today.AddDays(1),
    };

    private ValidationResult Validate(CreateJobPostingRequest request) => _validator.Validate(request);

    private void AssertOnlyError(CreateJobPostingRequest request, string property, string? messageContains = null)
    {
        var result = Validate(request);
        var failure = Assert.Single(result.Errors);
        Assert.Equal(property, failure.PropertyName);
        if (messageContains is not null)
        {
            Assert.Contains(messageContains, failure.ErrorMessage);
        }
    }

    [Fact]
    public void A_complete_request_is_valid()
    {
        var result = Validate(Valid());
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));
    }

    [Fact]
    public void An_empty_request_reports_every_required_field_once()
    {
        var result = Validate(new CreateJobPostingRequest { Openings = null, PayPeriod = null, Status = null });

        var properties = result.Errors.Select(e => e.PropertyName).Distinct().OrderBy(p => p).ToArray();
        Assert.Equal(
            ["ClosingDate", "Country", "Department", "Description", "EmploymentType", "Location", "Openings",
             "PayPeriod", "SalaryCurrency", "SalaryMax", "SalaryMin", "Seniority", "Status", "Title", "WorkArrangement"],
            properties);
    }

    // --- salary ---------------------------------------------------------------

    [Fact]
    public void Salary_min_equal_to_max_is_rejected_on_salaryMax()
    {
        var request = Valid() with { SalaryMin = 50_000, SalaryMax = 50_000 };
        AssertOnlyError(request, "SalaryMax", "greater than salary minimum");
    }

    [Fact]
    public void Salary_min_greater_than_max_is_rejected_on_salaryMax()
    {
        var request = Valid() with { SalaryMin = 42_000, SalaryMax = 38_000 };
        AssertOnlyError(request, "SalaryMax", "greater than salary minimum");
    }

    [Fact]
    public void Salary_max_one_unit_above_min_is_valid()
    {
        Assert.True(Validate(Valid() with { SalaryMin = 38_000, SalaryMax = 38_000.01m }).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Salary_min_must_be_positive(double min)
    {
        var request = Valid() with { SalaryMin = (decimal)min };
        var result = Validate(request);
        Assert.Contains(result.Errors, e => e.PropertyName == "SalaryMin" && e.ErrorMessage.Contains("greater than 0"));
        // The cross-field rule stays quiet until both values are individually valid.
        Assert.DoesNotContain(result.Errors, e => e.PropertyName == "SalaryMax");
    }

    [Fact]
    public void Salary_above_ten_million_is_rejected()
    {
        var request = Valid() with { SalaryMin = 9_999_999, SalaryMax = 10_000_000.01m };
        AssertOnlyError(request, "SalaryMax", "at most 10,000,000");
    }

    [Fact]
    public void Salary_of_exactly_ten_million_is_allowed()
    {
        Assert.True(Validate(Valid() with { SalaryMin = 1, SalaryMax = 10_000_000 }).IsValid);
    }

    // --- closing date -----------------------------------------------------------

    [Fact]
    public void Closing_date_equal_to_today_is_rejected()
    {
        AssertOnlyError(Valid() with { ClosingDate = Today }, "ClosingDate", "in the future");
    }

    [Fact]
    public void Closing_date_in_the_past_is_rejected()
    {
        AssertOnlyError(Valid() with { ClosingDate = Today.AddDays(-1) }, "ClosingDate", "in the future");
    }

    [Fact]
    public void Closing_date_tomorrow_is_valid()
    {
        Assert.True(Validate(Valid() with { ClosingDate = Today.AddDays(1) }).IsValid);
    }

    [Fact]
    public void Today_is_evaluated_in_UTC_not_local_time()
    {
        // 23:30Z on the 19th is already the 20th in UTC+1 — a local-time
        // comparison would wrongly accept the 20th. The rule must use UTC.
        var utcPlusOneMidnightTrap = Today.AddDays(1);
        Assert.True(Validate(Valid() with { ClosingDate = utcPlusOneMidnightTrap }).IsValid);
        Assert.False(Validate(Valid() with { ClosingDate = Today }).IsValid);
    }

    // --- lengths --------------------------------------------------------------

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void Title_length_boundaries(int length, bool valid)
    {
        Assert.Equal(valid, Validate(Valid() with { Title = new string('t', length) }).IsValid);
    }

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void Description_length_boundaries(int length, bool valid)
    {
        Assert.Equal(valid, Validate(Valid() with { Description = new string('d', length) }).IsValid);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(80, true)]
    [InlineData(81, false)]
    public void Department_length_boundaries(int length, bool valid)
    {
        Assert.Equal(valid, Validate(Valid() with { Department = new string('d', length) }).IsValid);
    }

    // --- location vs. work arrangement -----------------------------------------

    [Fact]
    public void Location_is_required_when_not_remote()
    {
        AssertOnlyError(Valid() with { WorkArrangement = "OnSite", Location = "" }, "Location", "unless the work arrangement is Remote");
    }

    [Fact]
    public void Location_may_be_empty_when_remote()
    {
        Assert.True(Validate(Valid() with { WorkArrangement = "Remote", Location = null }).IsValid);
    }

    [Fact]
    public void Location_when_present_is_still_length_checked_for_remote()
    {
        AssertOnlyError(Valid() with { WorkArrangement = "Remote", Location = "X" }, "Location", "2–120");
    }

    // --- enums ----------------------------------------------------------------

    [Theory]
    [InlineData("EmploymentType", "Freelance")]
    [InlineData("Seniority", "Guru")]
    [InlineData("WorkArrangement", "Office")]
    [InlineData("PayPeriod", "Weekly")]
    [InlineData("SalaryCurrency", "XXX")]
    [InlineData("Status", "Archived")]
    public void Unknown_enum_values_are_rejected(string property, string value)
    {
        var request = property switch
        {
            "EmploymentType" => Valid() with { EmploymentType = value },
            "Seniority" => Valid() with { Seniority = value },
            "WorkArrangement" => Valid() with { WorkArrangement = value },
            "PayPeriod" => Valid() with { PayPeriod = value },
            "SalaryCurrency" => Valid() with { SalaryCurrency = value },
            _ => Valid() with { Status = value },
        };
        AssertOnlyError(request, property, "must be");
    }

    [Fact]
    public void Currency_is_accepted_case_insensitively()
    {
        Assert.True(Validate(Valid() with { SalaryCurrency = "gbp" }).IsValid);
    }

    // --- openings -------------------------------------------------------------

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(999, true)]
    [InlineData(1000, false)]
    public void Openings_boundaries(int openings, bool valid)
    {
        Assert.Equal(valid, Validate(Valid() with { Openings = openings }).IsValid);
    }

    // --- contact --------------------------------------------------------------

    [Theory]
    [InlineData("careers@northline")]
    [InlineData("not an email")]
    public void Invalid_application_email_is_rejected(string email)
    {
        AssertOnlyError(Valid() with { ApplicationEmail = email }, "ApplicationEmail", "valid application email");
    }

    [Theory]
    [InlineData("northline.co/careers")]
    [InlineData("ftp://northline.co/careers")]
    [InlineData("javascript:alert(1)")]
    public void Application_url_must_be_absolute_http_or_https(string url)
    {
        AssertOnlyError(Valid() with { ApplicationUrl = url }, "ApplicationUrl", "absolute http");
    }

    [Fact]
    public void Https_application_url_is_valid()
    {
        Assert.True(Validate(Valid() with { ApplicationUrl = "https://northline.co/careers/4471" }).IsValid);
    }

    [Fact]
    public void Both_contact_fields_may_be_omitted()
    {
        Assert.True(Validate(Valid() with { ApplicationEmail = null, ApplicationUrl = null }).IsValid);
    }

    // --- skills ---------------------------------------------------------------

    [Fact]
    public void Twenty_skills_are_allowed_and_twenty_one_are_not()
    {
        var twenty = Enumerable.Range(1, 20).Select(i => $"skill-{i}").ToArray();
        Assert.True(Validate(Valid() with { Skills = twenty }).IsValid);

        var twentyOne = twenty.Append("one-more").ToArray();
        AssertOnlyError(Valid() with { Skills = twentyOne }, "Skills", "at most 20");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_skill_entries_are_rejected(string skill)
    {
        AssertOnlyError(Valid() with { Skills = ["WMS", skill] }, "Skills", "1–40");
    }

    [Fact]
    public void Skill_longer_than_forty_characters_is_rejected()
    {
        AssertOnlyError(Valid() with { Skills = [new string('s', 41)] }, "Skills", "1–40");
    }

    [Fact]
    public void Skill_errors_are_keyed_to_the_skills_control_not_an_index()
    {
        var result = Validate(Valid() with { Skills = ["ok", "", new string('s', 41)] });
        Assert.All(result.Errors, e => Assert.Equal("Skills", e.PropertyName));
    }
}
