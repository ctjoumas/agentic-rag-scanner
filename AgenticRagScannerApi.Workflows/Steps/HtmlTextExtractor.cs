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
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
