using System.ComponentModel.DataAnnotations;

namespace TalentBridge.Post.Api.Configuration;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Tests turn the background loop off and drive the publisher directly.</summary>
    public bool Enabled { get; init; } = true;

    [Range(1, 3_600)] public int PollIntervalSeconds { get; init; } = 2;
    [Range(1, 500)] public int BatchSize { get; init; } = 20;

    /// <summary>
    /// Genuine delivery failures before a row is parked. Parked rows are never
    /// deleted: they stay unprocessed, are reported by /health/ready, and resume
    /// when <c>attempts</c> is reset.
    /// </summary>
    [Range(1, 1_000)] public int MaxAttempts { get; init; } = 10;

    /// <summary>An unprocessed row older than this means the outbox is backed up.</summary>
    [Range(1, 1_440)] public int BackedUpAfterMinutes { get; init; } = 2;
}
