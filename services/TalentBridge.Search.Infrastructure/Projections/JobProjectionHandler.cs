using Dapper;
using Npgsql;

namespace TalentBridge.Search.Infrastructure.Projections;

/// <summary>
/// Applies a projection message to <c>search.job_listings</c> as one upsert.
/// Idempotent by construction: the <c>WHERE</c> on the conflict branch rejects any
/// message whose version is not strictly newer than the stored row, so replays
/// and out-of-order deliveries are harmless.
/// </summary>
public sealed class JobProjectionHandler(NpgsqlDataSource dataSource)
{
    private const string Upsert = """
        insert into search.job_listings (
            id, slug, title, department, location, country, work_arrangement, employment_type, seniority,
            salary_min, salary_max, salary_currency, pay_period, salary_visible,
            description, responsibilities, requirements, skills, organization,
            application_url, application_email, closing_date, published_at, is_open, version, projected_at)
        values (
            @Id, @Slug, @Title, @Department, @Location, @Country, @WorkArrangement, @EmploymentType, @Seniority,
            @SalaryMin, @SalaryMax, @SalaryCurrency, @PayPeriod, @SalaryVisible,
            @Description, @Responsibilities, @Requirements, @Skills, @Organization,
            @ApplicationUrl, @ApplicationEmail, @ClosingDate, @PublishedAt, @IsOpen, @Version, now())
        on conflict (id) do update set
            slug = excluded.slug,
            title = excluded.title,
            department = excluded.department,
            location = excluded.location,
            country = excluded.country,
            work_arrangement = excluded.work_arrangement,
            employment_type = excluded.employment_type,
            seniority = excluded.seniority,
            salary_min = excluded.salary_min,
            salary_max = excluded.salary_max,
            salary_currency = excluded.salary_currency,
            pay_period = excluded.pay_period,
            salary_visible = excluded.salary_visible,
            description = excluded.description,
            responsibilities = excluded.responsibilities,
            requirements = excluded.requirements,
            skills = excluded.skills,
            organization = excluded.organization,
            application_url = excluded.application_url,
            application_email = excluded.application_email,
            closing_date = excluded.closing_date,
            published_at = excluded.published_at,
            is_open = excluded.is_open,
            version = excluded.version,
            projected_at = now()
        where job_listings.version < excluded.version
        """;

    /// <summary>Returns true when the row was inserted or updated, false when the message was stale or a replay.</summary>
    public async Task<bool> ApplyAsync(JobProjectionMessage message, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var affected = await connection.ExecuteAsync(new CommandDefinition(Upsert, message, cancellationToken: ct));
        return affected == 1;
    }
}
