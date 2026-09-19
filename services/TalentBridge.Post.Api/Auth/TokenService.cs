using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TalentBridge.Post.Api.Configuration;
using TalentBridge.Post.Domain.Managers;

namespace TalentBridge.Post.Api.Auth;

/// <summary>
/// Mints access tokens (JWT, HS256, 15 min) and refresh tokens (opaque 256-bit
/// random, only the SHA-256 hash is stored).
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public const string ClaimOrganization = "org";

    private readonly JwtOptions _jwt = options.Value;
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_jwt.RefreshTokenDays);

    public string CreateAccessToken(Manager manager)
    {
        var now = clock.GetUtcNow();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(_jwt.AccessTokenMinutes).UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = manager.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = manager.Email,
                [JwtRegisteredClaimNames.Name] = manager.FullName,
                [ClaimOrganization] = manager.Organization,
                [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString(),
            },
            SigningCredentials = new SigningCredentials(SigningKey(_jwt), SecurityAlgorithms.HmacSha256),
        };

        return _handler.CreateToken(descriptor);
    }

    /// <summary>Returns the raw token to hand to the client and the hash to persist.</summary>
    public static (string Raw, string Hash) CreateRefreshToken()
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (raw, HashRefreshToken(raw));
    }

    public static string HashRefreshToken(string raw) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    public static SymmetricSecurityKey SigningKey(JwtOptions jwt) =>
        new(Encoding.UTF8.GetBytes(jwt.SigningSecret));

    public static Guid? ManagerIdFrom(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
}
