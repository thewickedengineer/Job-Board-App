namespace TalentBridge.Post.Domain.Outbox;

/// <summary>
/// Transactional outbox row. Written in the same <c>SaveChangesAsync</c> as the
/// aggregate change it describes; delivered later by the background publisher.
/// </summary>
public sealed class OutboxMessage
{
    public long Id { get; private set; }
    public Guid AggregateId { get; private set; }
    public string Type { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage Create(Guid aggregateId, string type, string jsonPayload, DateTimeOffset now) =>
        new()
        {
            AggregateId = aggregateId,
            Type = type,
            Payload = jsonPayload,
            OccurredAt = now,
            Attempts = 0,
        };

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length <= 2000 ? error : error[..2000];
    }
}
