using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalentBridge.Post.Domain.JobPostings;

namespace TalentBridge.Post.Infrastructure.Persistence.Configurations;

internal sealed class JobPostingConfiguration : IEntityTypeConfiguration<JobPosting>
{
    public void Configure(EntityTypeBuilder<JobPosting> b)
    {
        b.ToTable("job_postings", t =>
        {
            // Database-level invariants. Validation enforces these first, but the
            // database is the last line of defence and must not trust callers.
            t.HasCheckConstraint("ck_job_postings_salary_range", "salary_min < salary_max");
            t.HasCheckConstraint("ck_job_postings_salary_positive", "salary_min > 0");
            t.HasCheckConstraint("ck_job_postings_openings", "openings > 0");
            t.HasCheckConstraint("ck_job_postings_status", InList("status", JobPostingVocabulary.Statuses));
            t.HasCheckConstraint("ck_job_postings_employment_type", InList("employment_type", JobPostingVocabulary.EmploymentTypes));
            t.HasCheckConstraint("ck_job_postings_seniority", InList("seniority", JobPostingVocabulary.Seniorities));
            t.HasCheckConstraint("ck_job_postings_work_arrangement", InList("work_arrangement", JobPostingVocabulary.WorkArrangements));
            t.HasCheckConstraint("ck_job_postings_pay_period", InList("pay_period", JobPostingVocabulary.PayPeriods));
        });

        b.HasKey(p => p.Id);

        b.HasOne(p => p.Manager)
            .WithMany()
            .HasForeignKey(p => p.ManagerId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(p => p.ReferenceCode).HasMaxLength(40);
        b.Property(p => p.Title).HasMaxLength(120).IsRequired();
        b.Property(p => p.Department).HasMaxLength(80).IsRequired();
        b.Property(p => p.EmploymentType).HasMaxLength(20).IsRequired();
        b.Property(p => p.Seniority).HasMaxLength(20).IsRequired();
        b.Property(p => p.Openings).IsRequired().HasDefaultValue(1);
        b.Property(p => p.WorkArrangement).HasMaxLength(20).IsRequired();
        b.Property(p => p.Location).HasMaxLength(120).IsRequired();
        b.Property(p => p.Country).HasMaxLength(80).IsRequired();

        b.Property(p => p.SalaryMin).HasPrecision(12, 2).IsRequired();
        b.Property(p => p.SalaryMax).HasPrecision(12, 2).IsRequired();
        b.Property(p => p.SalaryCurrency).HasColumnType("char(3)").IsRequired().HasDefaultValue("CAD");
        b.Property(p => p.PayPeriod).HasMaxLength(20).IsRequired().HasDefaultValue("Annual");
        b.Property(p => p.SalaryVisible).IsRequired().HasDefaultValue(true);

        b.Property(p => p.Description).IsRequired();
        b.Property(p => p.Responsibilities);
        b.Property(p => p.Requirements);
        b.Property(p => p.Skills).HasColumnType("text[]").IsRequired();

        b.Property(p => p.ApplicationUrl).HasMaxLength(2048);
        b.Property(p => p.ApplicationEmail).HasMaxLength(320);
        b.Property(p => p.ClosingDate).IsRequired();

        b.Property(p => p.Status).HasMaxLength(20).IsRequired();
        b.Property(p => p.Slug).HasMaxLength(160).IsRequired();
        b.HasIndex(p => p.Slug).IsUnique();

        b.Property(p => p.CreatedAt).IsRequired();
        b.Property(p => p.UpdatedAt).IsRequired();
        b.Property(p => p.PublishedAt);

        b.Property(p => p.Version).IsRequired().HasDefaultValue(1).IsConcurrencyToken();

        // The dashboard lists a manager's own postings newest-first.
        b.HasIndex(p => new { p.ManagerId, p.CreatedAt }).IsDescending(false, true);
    }

    private static string InList(string column, IEnumerable<string> values) =>
        $"{column} in ({string.Join(", ", values.Select(v => $"'{v}'"))})";
}
