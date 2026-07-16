using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <summary>
/// Pure HTML → readable-text extraction for the Fetch & Clean step (Epic 5, story 5.2). Strips the
/// boilerplate (script/style/nav/header/footer/etc.) that would otherwise pollute the cleaned full text
/// handed to the Relevance Eval agent, then collapses whitespace. Deliberately simple and deterministic
/// so it can be unit-tested without a network.
/// </summary>
internal static partial class HtmlTextExtractor
{
    // Structural / non-content elements whose text is never part of the document body.
    private static readonly string[] BoilerplateTags =
    {
        "script", "style", "noscript", "template", "svg", "canvas", "iframe",
        "nav", "header", "footer", "aside", "form", "button", "input", "select",
        "figure", "figcaption",
    };

    /// <summary>
    /// Parses <paramref name="html"/>, removes boilerplate, and returns normalized visible text
    /// (empty string when nothing meaningful remains).
    /// </summary>
    public static string ExtractText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);

        // Harvest labeled date signals from the WHOLE document BEFORE stripping. Publication/updated dates
        // live in <head> <meta> tags and header/metadata blocks (<dl>, <time>) that the boilerplate removal
        // and <main>-only selection below deliberately discard - so capture them first and prepend them,
        // keeping the aggressive chrome-stripping intact. Whatever LABEL the page used passes through
        // verbatim (Published, Updated, first-published-at, ...); the downstream eval interprets them.
        var dateLines = HarvestDateLines(document);

        // Drop comments and known boilerplate subtrees.
        var toRemove = document.DocumentNode
            .Descendants()
            .Where(n => n.NodeType == HtmlNodeType.Comment ||
                        BoilerplateTags.Contains(n.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var node in toRemove)
        {
            node.Remove();
        }

        // Prefer <main>/<article> when present (the real content), else fall back to <body>/document.
        var root = document.DocumentNode.SelectSingleNode("//main")
                   ?? document.DocumentNode.SelectSingleNode("//article")
                   ?? document.DocumentNode.SelectSingleNode("//body")
                   ?? document.DocumentNode;

        var decoded = HtmlEntity.DeEntitize(root.InnerText ?? string.Empty);
        var body = WhitespaceRegex().Replace(decoded, " ").Trim();

        if (dateLines.Count == 0)
        {
            return body;
        }

        // Prepend the harvested dates so they survive the eval's head-truncation and are easy to read.
        var builder = new StringBuilder();
        builder.AppendLine("Dates found on page:");
        foreach (var line in dateLines)
        {
            builder.AppendLine(line);
        }

        if (body.Length > 0)
        {
            builder.AppendLine();
            builder.Append(body);
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Collects date signals (label + ISO date) from the structured carriers where dates reliably appear
    /// as discrete values - <c>&lt;meta content&gt;</c>, <c>&lt;time datetime&gt;</c>, and
    /// <c>&lt;dt&gt;/&lt;dd&gt;</c> pairs - across the WHOLE document. A value is kept only when the
    /// framework date parser accepts it (no regex, no hardcoded label names), so the page may call it
    /// "Published", "Updated", "Refreshed on", or "first-published-at" and the label passes through
    /// verbatim. Deduplicated by label + date.
    /// </summary>
    private static IReadOnlyList<string> HarvestDateLines(HtmlDocument document)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Consider(string? label, string? rawValue)
        {
            if (!TryExtractDate(rawValue, out var iso))
            {
                return;
            }

            var cleanLabel = NormalizeLabel(label);
            if (!seen.Add($"{cleanLabel}|{iso}"))
            {
                return;
            }

            lines.Add(cleanLabel.Length == 0 ? $"- {iso}" : $"- {cleanLabel}: {iso}");
        }

        foreach (var meta in document.DocumentNode.SelectNodes("//meta") ?? Enumerable.Empty<HtmlNode>())
        {
            var content = meta.GetAttributeValue("content", null);
            var label = meta.GetAttributeValue("name", null) ?? meta.GetAttributeValue("property", null);
            Consider(label, content);
        }

        foreach (var time in document.DocumentNode.SelectNodes("//time") ?? Enumerable.Empty<HtmlNode>())
        {
            var datetime = time.GetAttributeValue("datetime", null);
            Consider("date", string.IsNullOrWhiteSpace(datetime) ? time.InnerText : datetime);
        }

        // <dl> definition lists: pair each <dd> with its nearest preceding <dt> (the human label).
        foreach (var dd in document.DocumentNode.SelectNodes("//dd") ?? Enumerable.Empty<HtmlNode>())
        {
            var term = dd.PreviousSibling;
            while (term is not null && !string.Equals(term.Name, "dt", StringComparison.OrdinalIgnoreCase))
            {
                term = term.PreviousSibling;
            }

            Consider(term?.InnerText, dd.InnerText);
        }

        return lines;
    }

    /// <summary>Collapses whitespace on a label and trims a trailing colon (e.g. "Published:" -> "Published").</summary>
    private static string NormalizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var text = WhitespaceRegex().Replace(HtmlEntity.DeEntitize(label), " ").Trim();
        return text.TrimEnd(':').Trim();
    }

    /// <summary>
    /// True when <paramref name="raw"/> begins with a parseable calendar date (ISO, or long-form like
    /// "22 May 2014"), returning it normalized to <c>yyyy-MM-dd</c>. Uses the framework date parser (not a
    /// regex) and tolerates trailing text (e.g. "15 July 2026 - See all updates") by trying successively
    /// shorter leading token spans. A plausible 4-digit year (1900-2100) must be present, which filters out
    /// times, version numbers, ids, and other non-date values.
    /// </summary>
    private static bool TryExtractDate(string? raw, out string iso)
    {
        iso = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var tokens = HtmlEntity.DeEntitize(raw).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // Trim from the END only: peel trailing tokens off until the leading span parses (e.g.
        // "15 July 2026 - See all updates" -> "15 July 2026"). We deliberately do NOT trim leading tokens.
        // The structured carriers we harvest (<meta content>, <time datetime>, <dd>) put the date first, so
        // leading junk before the date (e.g. "on 15 July 2026") would be a rare case, and handling it would
        // risk pulling an unrelated date out of the middle of prose - so it is intentionally left unhandled.
        for (var count = tokens.Length; count >= 1; count--)
        {
            var candidate = string.Join(' ', tokens, 0, count);
            if (!ContainsPlausibleYear(candidate))
            {
                continue;
            }

            if (DateTimeOffset.TryParse(
                    candidate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
                    out var parsed))
            {
                iso = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the text contains a 4-digit run that reads as a plausible year (1900-2100).</summary>
    private static bool ContainsPlausibleYear(string text)
    {
        var run = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                run++;
                continue;
            }

            if (run == 4 &&
                int.TryParse(text.AsSpan(i - 4, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
                year is >= 1900 and <= 2100)
            {
                return true;
            }

            run = 0;
        }

        return false;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
