using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace One.Infrastructure.Services.Internal;

/// <summary>Deriva identificadores legibles en URL a partir de un nombre libre.</summary>
public static partial class Slugger
{
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    public static string From(string value, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        var slug = NonSlugChars()
            .Replace(builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant(), "-")
            .Trim('-');

        return slug.Length <= maxLength ? slug : slug[..maxLength].Trim('-');
    }

    /// <summary>Añade un sufijo numérico hasta encontrar un slug libre.</summary>
    public static string MakeUnique(string baseSlug, Func<string, bool> exists, int maxLength = 80)
    {
        if (!exists(baseSlug)) return baseSlug;

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{baseSlug}-{suffix}";
            if (candidate.Length > maxLength)
                candidate = $"{baseSlug[..(maxLength - suffix.ToString().Length - 1)]}-{suffix}";

            if (!exists(candidate)) return candidate;
        }

        return $"{baseSlug}-{Guid.CreateVersion7().ToString("n")[..6]}";
    }
}
