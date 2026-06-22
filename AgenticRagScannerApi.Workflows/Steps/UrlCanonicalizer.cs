using System.Diagnostics.CodeAnalysis;

namespace AgenticRagScannerApi.Workflows.Steps;

/// <summary>
/// Pure, deterministic URL canonicalization for the pre-filter (story 4.3). Produces a stable dedupe
/// <em>key</em> for an absolute http(s) URL, or reports the URL as invalid. The canonicalization rules
/// are intentionally <strong>conservative</strong> - we only collapse forms that are virtually always
/// the same page, because in a compliance context a false "duplicate" silently drops a regulatory item.
///
/// <para>Rules (documented because they affect recall):</para>
/// <list type="bullet">
///   <item>Only absolute <c>http</c>/<c>https</c> URLs are valid; everything else is rejected.</item>
///   <item>Scheme and host are lowercased; a leading <c>www.</c> on the host is removed.</item>
///   <item>The fragment (<c>#...</c>) is dropped - it never selects a different server document.</item>
///   <item>Well-known tracking query parameters (utm_*, gclid, fbclid, msclkid, mc_eid) are removed;
///         all other query parameters are preserved (they can select distinct content) and sorted so
///         order does not create false uniques.</item>
///   <item>A trailing <c>/</c> on the path is trimmed (but the root path stays a single <c>/</c>).</item>
/// </list>
/// </summary>
internal static class UrlCanonicalizer
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "gclid",
        "fbclid",
        "msclkid",
        "mc_eid",
        "_ga",
    };

    /// <summary>
    /// Attempts to canonicalize <paramref name="url"/> into a dedupe key. Returns false (with a null
    /// <paramref name="key"/>) when the URL is not a valid absolute http(s) URL.
    /// </summary>
    public static bool TryCanonicalize(string? url, [NotNullWhen(true)] out string? key)
    {
        key = null;

        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host["www.".Length..];
        }

        if (host.Length == 0)
        {
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length == 0)
        {
            path = "/";
        }

        var query = CanonicalizeQuery(uri.Query);

        key = string.Concat(host, path, query).ToLowerInvariant();
        return true;
    }

    /// <summary>Drops tracking parameters and sorts the remainder so order is not significant.</summary>
    private static string CanonicalizeQuery(string rawQuery)
    {
        if (string.IsNullOrEmpty(rawQuery) || rawQuery == "?")
        {
            return string.Empty;
        }

        var pairs = rawQuery.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p =>
            {
                var name = p.Split('=', 2)[0];
                return !TrackingParameters.Contains(name) &&
                       !name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        return pairs.Length == 0 ? string.Empty : "?" + string.Join('&', pairs);
    }
}
