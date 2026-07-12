using System.Globalization;
using System.Text.RegularExpressions;

namespace CrossdexBar.Providers.Ollama;

/// <summary>
/// Regex-scrapes the (undocumented, HTML) Ollama Cloud Usage section on <c>ollama.com/settings</c> — ported
/// from Win-CodexBar's <c>parse_usage_block</c>/<c>usage_block_end</c>/<c>parse_first_datetime</c>
/// (rust/src/providers/ollama/mod.rs, 2026-07). No full HTML parser: find a label ("Session usage" etc.),
/// take the text up to whichever other label appears next (capped at 4000 chars) as the search window, then
/// look for "NN% used" first and a progress-bar "width: NN%" CSS value as a fallback.
/// </summary>
internal static partial class OllamaUsagePageParser
{
    private static readonly string[] AllLabels = ["Session usage", "Hourly usage", "Weekly usage"];

    public readonly record struct Block(double UsedPercent, DateTimeOffset? ResetsAt);

    public static Block? ParseBlock(string html, params string[] labels)
    {
        foreach (var label in labels)
        {
            var pos = html.IndexOf(label, StringComparison.Ordinal);
            if (pos < 0)
                continue;

            var tail = html[pos..];
            var end = Math.Min(UsageBlockEnd(tail, label) ?? tail.Length, 4000);
            var window = tail[..Math.Min(end, tail.Length)];

            var usedMatch = UsedPercentRegex().Match(window);
            if (usedMatch.Success && double.TryParse(usedMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var usedPercent))
                return new Block(usedPercent, ParseFirstDateTime(window));

            var widthMatch = WidthPercentRegex().Match(window);
            if (widthMatch.Success && double.TryParse(widthMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var widthPercent))
                return new Block(widthPercent, ParseFirstDateTime(window));
        }

        return null;
    }

    private static int? UsageBlockEnd(string tail, string currentLabel)
    {
        if (currentLabel.Length > tail.Length)
            return null;

        var searchArea = tail[currentLabel.Length..];
        int? earliest = null;
        foreach (var label in AllLabels)
        {
            if (label == currentLabel)
                continue;

            var idx = searchArea.IndexOf(label, StringComparison.Ordinal);
            if (idx < 0)
                continue;

            var absolute = idx + currentLabel.Length;
            if (earliest is null || absolute < earliest)
                earliest = absolute;
        }

        return earliest;
    }

    private static DateTimeOffset? ParseFirstDateTime(string window)
    {
        var match = DateTimeRegex().Match(window);
        return match.Success
            && DateTimeOffset.TryParse(match.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*%\s*used")]
    private static partial Regex UsedPercentRegex();

    [GeneratedRegex(@"width:\s*(\d+(?:\.\d+)?)%")]
    private static partial Regex WidthPercentRegex();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})")]
    private static partial Regex DateTimeRegex();
}
