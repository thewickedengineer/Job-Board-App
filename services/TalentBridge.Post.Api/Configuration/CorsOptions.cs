using System.ComponentModel.DataAnnotations;

namespace TalentBridge.Post.Api.Configuration;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "web";

    /// <summary>Explicit browser origins. Never a wildcard: the refresh cookie needs credentials.</summary>
    [MinLength(1, ErrorMessage = "Cors:AllowedOrigins must list at least one origin.")]
    public string[] AllowedOrigins { get; init; } = [];
}
