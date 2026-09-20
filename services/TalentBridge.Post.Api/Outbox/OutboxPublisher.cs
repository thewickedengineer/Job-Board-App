using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TalentBridge.Post.Api.Configuration;
using TalentBridge.Post.Domain.Outbox;
using TalentBridge.Post.Infrastructure.Persistence;

namespace TalentBridge.Post.Api.Outbox;

/// <summary>
/// Drains <c>post.outbox_messages</c> to the Search API. Rows are claimed with
/// <c>FOR UPDATE SKIP LOCKED</c> so several API instances can run the loop
/// without double-delivery; the Search side is idempotent anyway, so the
/// guarantee here is at-least-once, never at-most-once.
/// </summary>
public sealed class OutboxPublisher(
    IServiceScopeFactory scopes,
    ProjectionClient client,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Outbox publisher disabled by configuration");
            return;
        }

        logger.LogInformation("Outbox publisher polling every {Seconds}s, batch {Batch}, max attempts {Max}",
            _options.PollIntervalSeconds, _options.BatchSize, _options.MaxAttempts);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds), clock);
        do
        {
            try
            {
                // Keep draining while full batches come back; sleep only when caught up.
                while (await ProcessBatchAsync(stoppingToken) == _options.BatchSize) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A database outage must not kill the loop; the next tick tries again.
                logger.LogError(ex, "Outbox poll failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Claims and delivers one batch. Returns the number of rows claimed. Public
    /// so tests can drive a single cycle without the timer.
    /// </summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PostDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var batch = await db.OutboxMessages
            .FromSql($"""
                select * from post.outbox_messages
                where processed_at is null and attempts < {_options.MaxAttempts}
                order by id
                limit {_options.BatchSize}
                for update skip locked
                """)
            .ToListAsync(ct);

        if (batch.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return 0;
        }

        foreach (var message in batch)
        {
            var outcome = await client.PushAsync(message, ct);
            switch (outcome.Status)
            {
                case ProjectionStatus.Delivered:
                    message.MarkProcessed(clock.GetUtcNow());
                    logger.LogInformation("Outbox message {MessageId} ({Type}) for {AggregateId} delivered", message.Id, message.Type, message.AggregateId);
                    break;

                case ProjectionStatus.Failed:
                    message.MarkFailed(outcome.Error!);
                    if (message.Attempts >= _options.MaxAttempts)
                    {
                        logger.LogError("Outbox message {MessageId} parked after {Attempts} attempts: {Error}", message.Id, message.Attempts, outcome.Error);
                    }
                    else
                    {
                        logger.LogWarning("Outbox message {MessageId} failed (attempt {Attempts}): {Error}", message.Id, message.Attempts, outcome.Error);
                    }

                    break;

                case ProjectionStatus.Skipped:
                    // Circuit open: leave the row untouched and stop the batch —
                    // every later message would hit the same open breaker.
                    logger.LogWarning("Search API circuit is open; deferring outbox message {MessageId} and the rest of the batch", message.Id);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return batch.Count;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }
}
