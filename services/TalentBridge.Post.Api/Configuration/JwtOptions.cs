using System.ComponentModel.DataAnnotations;

namespace TalentBridge.Post.Api.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC-SHA256 key. Must be at least 32 bytes; lives in .env only.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Jwt:SigningSecret is required.")]
    [MinLength(32, ErrorMessage = "Jwt:SigningSecret must be at least 32 characters.")]
    public string SigningSecret { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; init; } = "talentbridge-post-api";

    [Required(AllowEmptyStrings = false)]
    public string Audience { get; init; } = "talentbridge-post-web";

    [Range(1, 120)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 14;
}
