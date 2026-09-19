namespace TalentBridge.Post.Domain.Managers;

/// <summary>
/// One opaque refresh token. Only the SHA-256 hash is stored; the raw value is
/// handed to the browser once and never persisted.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid ManagerId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private RefreshToken() { }

    internal static RefreshToken Issue(Guid managerId, string tokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            ManagerId = managerId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now)
    {
        if (RevokedAt is null)
        {
            RevokedAt = now;
        }
    }
}
