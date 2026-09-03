using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClickBaitThumbnailGenerator;

public static partial class ScenarioUtilities
{
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
            }
        }

        return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    public static bool IsNearDuplicate(string left, string right, double threshold = 0.82)
    {
        var leftTokens = Normalize(left).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = Normalize(right).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (leftTokens.Count == 0 || rightTokens.Count == 0) return false;

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.Ordinal).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.Ordinal).Count();
        return (double)intersection / union >= threshold;
    }

    public static string Filename(string scenarioId)
    {
        if (!ScenarioIdRegex().IsMatch(scenarioId))
            throw new ArgumentException("Scenario IDs must use the form cb-000001.", nameof(scenarioId));
        return $"{scenarioId}.webp";
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^cb-\d{6,}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScenarioIdRegex();
}
