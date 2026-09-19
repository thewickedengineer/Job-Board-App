using TalentBridge.Post.Domain.Managers;

namespace TalentBridge.Post.Api.Auth;

public sealed record SignupRequest(string Email, string Password, string FullName, string Organization);

public sealed record LoginRequest(string Email, string Password);

/// <summary>Body form of the refresh token, for non-browser clients. Browsers send the cookie instead.</summary>
public sealed record RefreshRequest(string? RefreshToken);

public sealed record ManagerResponse(
    Guid Id,
    string Email,
    string FullName,
    string Organization,
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt)
{
    public static ManagerResponse From(Manager m) =>
        new(m.Id, m.Email, m.FullName, m.Organization, m.EmailVerified, m.CreatedAt, m.LastLoginAt);
}

public sealed record AuthResponse(string AccessToken, string RefreshToken, ManagerResponse Manager);

public sealed record TokenPairResponse(string AccessToken, string RefreshToken);
