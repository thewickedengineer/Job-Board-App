using System.ComponentModel.DataAnnotations;

namespace TalentBridge.Post.Api.Configuration;

/// <summary>Where and how the outbox publisher pushes projections to the Search API.</summary>
public sealed class ProjectionOptions
{
    public const string SectionName = "Projection";
    public const string HeaderName = "X-Projection-Secret";

    [Required(AllowEmptyStrings = false, ErrorMessage = "Projection:SearchApiBaseUrl is required.")]
    [Url(ErrorMessage = "Projection:SearchApiBaseUrl must be an absolute URL.")]
    public string SearchApiBaseUrl { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "Projection:SharedSecret is required.")]
    [MinLength(32, ErrorMessage = "Projection:SharedSecret must be at least 32 characters.")]
    public string SharedSecret { get; init; } = string.Empty;
}
