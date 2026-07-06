using System.Text;
using AgenticRagScannerApi.Core.Contracts;

namespace AgenticRagScannerApi.Workflows.Prompts;

/// <summary>
/// Renders the topic group's carried regulatory updates (source URL, effective-date-aware dates, and the
/// vetted full-text snapshot) into a single block that is shared verbatim by the group-level Impact Area,
/// Tags, and Deloitte View prompts. Building it once - with a <em>group-wide</em> full-text budget spread
/// across the items rather than a fixed per-item cap - keeps the three calls grounded on exactly the same
/// context, avoids re-clipping the same text three times, and bounds the aggregate token cost regardless
/// of how many updates a group carried.
/// </summary>
internal static class AggregateContextBuilder
{
    /// <summary>
    /// Group-wide full-text budget (characters) spread across the carried items. A regulatory page's head
    /// carries the title, dates, and substance, so head-truncating each item's share is acceptable for
    /// classification and consolidated summarization.
    /// </summary>
    public const int DefaultTotalFullTextBudget = 48000;

    /// <summary>Never give an item less than this many characters, even in a large group.</summary>
    private const int MinCharsPerItem = 2000;

    /// <summary>
    /// Builds the "regulatory updates found for this topic group" block. Each item's full-text share is
    /// <c>totalFullTextBudget / itemCount</c> (floored at <see cref="MinCharsPerItem"/>), head-truncated.
    /// </summary>
    public static string BuildUpdatesBlock(
        IReadOnlyList<ResultItem> items,
        IReadOnlyDictionary<string, string?> fullTextByItemId,
        int totalFullTextBudget = DefaultTotalFullTextBudget)
    {
        var perItemBudget = Math.Max(MinCharsPerItem, totalFullTextBudget / Math.Max(1, items.Count));

        var builder = new StringBuilder();
        builder.AppendLine($"=== Regulatory updates found for this topic group ({items.Count}) - consider ALL of them ===");

        var index = 1;
        foreach (var item in items)
        {
            builder.AppendLine($"Update {index++}:");
            builder.AppendLine($"  Source URL: {(item.SourceUrls.Count > 0 ? item.SourceUrls[0] : "(none)")}");

            if (!string.IsNullOrWhiteSpace(item.EvalRationale))
            {
                builder.AppendLine($"  Relevance notes: {item.EvalRationale}");
            }

            var dates = FormatDates(item);
            if (dates is not null)
            {
                builder.AppendLine($"  Dates: {dates}");
            }

            var fullText = fullTextByItemId.TryGetValue(item.Id, out var text) ? text : null;
            builder.AppendLine(
                $"  Full text: {(string.IsNullOrWhiteSpace(fullText) ? "(full text unavailable - the fetch failed)" : Clip(fullText, perItemBudget))}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>Formats the item's effective-date-aware fields into a compact hint, or null when none are known.</summary>
    private static string? FormatDates(ResultItem item)
    {
        var parts = new List<string>(4);

        if (item.PublicationDate is { } published)
        {
            parts.Add($"published {published:yyyy-MM-dd}");
        }

        if (item.EffectiveDate is { } effective)
        {
            parts.Add($"effective {effective:yyyy-MM-dd}");
        }

        if (item.AppliesFrom is { } from)
        {
            parts.Add($"applies from {from:yyyy-MM-dd}");
        }

        if (item.AppliesTo is { } to)
        {
            parts.Add($"applies to {to:yyyy-MM-dd}");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string Clip(string text, int maxChars) =>
        text.Length > maxChars ? text[..maxChars] : text;
}
