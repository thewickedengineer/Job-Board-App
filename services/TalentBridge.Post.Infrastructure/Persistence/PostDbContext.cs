using Microsoft.EntityFrameworkCore;
using TalentBridge.Post.Domain.JobPostings;
using TalentBridge.Post.Domain.Managers;
using TalentBridge.Post.Domain.Outbox;

namespace TalentBridge.Post.Infrastructure.Persistence;

/// <summary>
/// The write-side unit of work. There is deliberately no repository layer over
/// this: EF Core's change tracker and <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>
/// already are the unit of work, and the outbox relies on that single transaction.
/// </summary>
public sealed class PostDbContext(DbContextOptions<PostDbContext> options) : DbContext(options)
{
    public const string Schema = "post";

    public DbSet<Manager> Managers => Set<Manager>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<JobPosting> JobPostings => Set<JobPosting>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PostDbContext).Assembly);
    }
}
