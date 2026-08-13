using System.Text.RegularExpressions;

namespace Mri.Core.Umo;

/// <summary>
/// Some hosts (mediafire) serve files only via short-lived tokenized URLs
/// behind a stable landing page. umo's direct handler needs the real file
/// URL, so the register step passes the emitted list through this resolver
/// just before handing it to umo: landing-page URLs in direct_download
/// fields are swapped for freshly resolved token URLs. The registered-list
/// hash is computed over the UNresolved list, so verification stays pure
/// (no network) and re-registration triggers only on real list changes.
/// </summary>
public sealed partial class EphemeralUrlResolver(HttpClient http)
{
    [GeneratedRegex(@"""direct_download"":\s*""(https://www\.mediafire\.com/file/[^""]+)""")]
    private static partial Regex MediafireLandingField();

    [GeneratedRegex(@"https://download[0-9]+\.mediafire\.com/[^""'\s]+")]
    private static partial Regex MediafireTokenUrl();

    /// <summary>Resolves a single URL; non-landing URLs pass through unchanged.</summary>
    public async Task<string> ResolveUrlAsync(string url, CancellationToken ct = default)
    {
        if (!url.StartsWith("https://www.mediafire.com/file/", StringComparison.OrdinalIgnoreCase))
            return url;
        var page = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        var token = MediafireTokenUrl().Match(page.Replace("&amp;", "&"));
        if (!token.Success)
            throw new InvalidOperationException(
                $"mediafire landing page yielded no download token: {url}");
        return token.Value;
    }

    /// <summary>Returns the list JSON with every resolvable landing URL replaced.</summary>
    public async Task<string> ResolveAsync(string umoListJson, CancellationToken ct = default)
    {
        foreach (Match m in MediafireLandingField().Matches(umoListJson))
        {
            var landing = System.Text.Json.JsonDocument
                .Parse($"{{\"u\":\"{m.Groups[1].Value}\"}}").RootElement.GetProperty("u").GetString()!;
            var token = await ResolveUrlAsync(landing, ct).ConfigureAwait(false);
            umoListJson = umoListJson.Replace(
                m.Value, m.Value.Replace(m.Groups[1].Value, token));
        }
        return umoListJson;
    }
}
