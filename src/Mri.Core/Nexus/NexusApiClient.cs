using System.Text.Json;

namespace Mri.Core.Nexus;

public sealed record NexusUser(string Name, bool IsPremium, bool IsSupporter, string? ProfileUrl);

/// <summary>
/// Minimal client for the Nexus Mods v1 REST API. Per the API Acceptable Use
/// Policy: Application-Name/Application-Version headers on every request, keys
/// stay on the user's machine, every call is user-initiated.
/// </summary>
public sealed class NexusApiClient(HttpClient http, string appVersion = "0.1.0")
{
    public const string BaseUrl = "https://api.nexusmods.com/v1/";

    /// <summary>Returns the account behind an API key, or null if the key is invalid.</summary>
    public async Task<NexusUser?> ValidateKeyAsync(string apiKey, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "users/validate.json");
        request.Headers.Add("apikey", apiKey.Trim());
        request.Headers.Add("Application-Name", "MorrowindRemakeInstaller");
        request.Headers.Add("Application-Version", appVersion);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            return null;
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = doc.RootElement;
        return new NexusUser(
            Name: root.GetProperty("name").GetString() ?? "unknown",
            IsPremium: root.TryGetProperty("is_premium", out var premium) && premium.GetBoolean(),
            IsSupporter: root.TryGetProperty("is_supporter", out var supporter) && supporter.GetBoolean(),
            ProfileUrl: root.TryGetProperty("profile_url", out var url) ? url.GetString() : null);
    }
}
