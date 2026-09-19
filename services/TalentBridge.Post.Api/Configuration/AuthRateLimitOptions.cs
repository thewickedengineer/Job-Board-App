using System.ComponentModel.DataAnnotations;

namespace TalentBridge.Post.Api.Configuration;

/// <summary>
/// Fixed-window, per-IP limits on the two unauthenticated credential endpoints.
/// Defaults are deliberately tight; tests raise them via configuration.
/// </summary>
public sealed class AuthRateLimitOptions
{
    public const string SectionName = "AuthRateLimit";
    public const string LoginPolicy = "auth-login";
    public const string SignupPolicy = "auth-signup";

    [Range(1, 10_000)] public int LoginPermitLimit { get; init; } = 5;
    [Range(1, 1_440)] public int LoginWindowMinutes { get; init; } = 5;
    [Range(1, 10_000)] public int SignupPermitLimit { get; init; } = 5;
    [Range(1, 1_440)] public int SignupWindowMinutes { get; init; } = 10;
}
