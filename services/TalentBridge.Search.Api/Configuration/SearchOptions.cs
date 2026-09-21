using System.ComponentModel.DataAnnotations;

namespace TalentBridge.Search.Api.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required(AllowEmptyStrings = false, ErrorMessage = "Database:ConnectionString is required.")]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Run db/search-schema.sql (idempotent) when the host starts.</summary>
    public bool ApplySchemaOnStartup { get; init; }
}

public sealed class ProjectionOptions
{
    public const string SectionName = "Projection";
    public const string HeaderName = "X-Projection-Secret";

    [Required(AllowEmptyStrings = false, ErrorMessage = "Projection:SharedSecret is required.")]
    [MinLength(32, ErrorMessage = "Projection:SharedSecret must be at least 32 characters.")]
    public string SharedSecret { get; init; } = string.Empty;
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "web";

    [MinLength(1, ErrorMessage = "Cors:AllowedOrigins must list at least one origin.")]
    public string[] AllowedOrigins { get; init; } = [];
}
