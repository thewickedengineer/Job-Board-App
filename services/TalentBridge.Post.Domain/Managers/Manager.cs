namespace TalentBridge.Post.Domain.Managers;

public sealed class Manager
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public string Organization { get; private set; } = null!;
    public bool EmailVerified { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    private readonly List<RefreshToken> _refreshTokens = [];
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens;

    private Manager() { }

    public static Manager Register(string email, string passwordHash, string fullName, string organization, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Email = email.Trim(),
            PasswordHash = passwordHash,
            FullName = fullName.Trim(),
            Organization = organization.Trim(),
            EmailVerified = false,
            CreatedAt = now,
        };

    public void RecordLogin(DateTimeOffset now) => LastLoginAt = now;

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public RefreshToken IssueRefreshToken(string tokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        var token = RefreshToken.Issue(Id, tokenHash, now, lifetime);
        _refreshTokens.Add(token);
        return token;
    }
}
