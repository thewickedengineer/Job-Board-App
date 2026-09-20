using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TalentBridge.Post.Api.Configuration;
using TalentBridge.Post.Infrastructure.Persistence;

namespace TalentBridge.Post.Api.Outbox;

/// <summary>
/// Readiness: the outbox is not backed up and nothing is parked. Parked rows
/// (attempts exhausted) are Unhealthy because a human has to look; a backlog is
/// Degraded because it usually clears itself once the Search API recovers.
/// </summary>
public sealed class OutboxHealthCheck(PostDbContext db, IOptions<OutboxOptions> options, TimeProvider clock) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var o = options.Value;
        var backedUpBefore = clock.GetUtcNow().AddMinutes(-o.BackedUpAfterMinutes);

        var pending = db.OutboxMessages.Where(m => m.ProcessedAt == null);
        var parked = await pending.CountAsync(m => m.Attempts >= o.MaxAttempts, ct);
        var overdue = await pending.CountAsync(m => m.Attempts < o.MaxAttempts && m.OccurredAt < backedUpBefore, ct);
        var total = await pending.CountAsync(ct);

        var data = new Dictionary<string, object>
        {
            ["pending"] = total,
            ["parked"] = parked,
            ["overdue"] = overdue,
            ["maxAttempts"] = o.MaxAttempts,
        };

        if (parked > 0)
        {
            return HealthCheckResult.Unhealthy($"{parked} outbox message(s) parked after {o.MaxAttempts} failed attempts.", data: data);
        }

        if (overdue > 0)
        {
            return HealthCheckResult.Degraded($"{overdue} outbox message(s) unprocessed for more than {o.BackedUpAfterMinutes} minutes.", data: data);
        }

        return HealthCheckResult.Healthy($"{total} pending.", data);
    }
}
