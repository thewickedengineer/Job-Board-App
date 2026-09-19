using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TalentBridge.Post.Infrastructure.Persistence;

namespace TalentBridge.Post.Api.JobPostings;

/// <summary>
/// Public, stable URL key: <c>senior-warehouse-supervisor-leeds</c>. Assigned once
/// at creation and never changed, so links on the job board survive edits.
/// </summary>
public static partial class SlugGenerator
{
    private const int MaxBaseLength = 140; // leaves room for a suffix inside the 160-char column

    public static string BaseSlug(string title, string location)
    {
        // "Leeds, West Yorkshire" → "leeds"; "Remote (UK)" → "remote-uk"
        var place = location.Split(',')[0];
        var slug = Slugify($"{title} {place}");
        return slug.Length <= MaxBaseLength ? slug : slug[..MaxBaseLength].TrimEnd('-');
    }

    /// <summary>The base slug, or the first free numeric suffix (-2, -3, …) when it is taken.</summary>
    public static async Task<string> NextAvailableAsync(PostDbContext db, string baseSlug, CancellationToken ct)
    {
        var taken = await db.JobPostings
            .Where(p => p.Slug == baseSlug || p.Slug.StartsWith(baseSlug + "-"))
            .Select(p => p.Slug)
            .ToListAsync(ct);

        if (taken.Count == 0)
        {
            return baseSlug;
        }

        var set = taken.ToHashSet(StringComparer.Ordinal);
        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{baseSlug}-{n}";
            if (!set.Contains(candidate))
            {
                return candidate;
            }
        }

        return WithRandomSuffix(baseSlug);
    }

    /// <summary>Fallback when two requests race for the same slug: a short random tail.</summary>
    public static string WithRandomSuffix(string baseSlug) =>
        $"{baseSlug}-{RandomNumberGenerator.GetHexString(6, lowercase: true)}";

    private static string Slugify(string value)
    {
        // Strip diacritics ("Zürich" → "Zurich") before collapsing to [a-z0-9-].
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var ascii = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                ascii.Append(c);
            }
        }

        var lowered = ascii.ToString().ToLowerInvariant();
        var hyphenated = NonSlugCharacters().Replace(lowered, "-");
        return hyphenated.Trim('-');
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugCharacters();
}
