using System.ComponentModel.DataAnnotations;

namespace TalentBridge.Post.Api.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required(AllowEmptyStrings = false, ErrorMessage = "Database:ConnectionString is required.")]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Apply pending EF Core migrations when the host starts. Honoured in the
    /// Development environment only; production applies migrations explicitly.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; init; }
}
