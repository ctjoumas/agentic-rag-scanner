using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <summary>
/// Pure PDF → text extraction for the Fetch &amp; Clean step (Epic 5, story 5.2). Reads page text in
/// reading order and collapses whitespace. Throwing parse errors are the caller's signal to fall back
/// to the Bing snippet (the document is never dropped).
/// </summary>
internal static partial class PdfTextExtractor
{
    /// <summary>Extracts normalized text from an in-memory PDF (empty string when no text is present).</summary>
    public static string ExtractText(byte[] data)
    {
        if (data is null || data.Length == 0)
        {
            return string.Empty;
        }

        using var document = PdfDocument.Open(data);

        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page);
            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.Append(text).Append('\n');
            }
        }

        return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
