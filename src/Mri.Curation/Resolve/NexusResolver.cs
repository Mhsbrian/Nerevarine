using System.Text.Json;

namespace Mri.Curation.Resolve;

/// <summary>
/// Curation-time Nexus API enrichment: mod metadata + file listings into the
/// resolve cache. Respects X-RL rate-limit headers — sleeps through hourly
/// exhaustion, aborts (resumably) when the daily budget runs dry.
/// </summary>
public sealed class NexusResolver(HttpClient http, string apiKey, Action<string>? log = null)
{
    private const string BaseUrl = "https://api.nexusmods.com/v1/";
    private int _hourlyRemaining = int.MaxValue;
    private int _dailyRemaining = int.MaxValue;
    private DateTimeOffset _hourlyReset = DateTimeOffset.MinValue;

    public sealed class DailyBudgetExhaustedException()
        : Exception("Nexus daily API budget exhausted — re-run tomorrow; the cache keeps everything fetched so far.");

    public async Task<CachedMod> ResolveAsync(int nexusId, CancellationToken ct = default)
    {
        var mod = await GetJsonAsync($"games/morrowind/mods/{nexusId}.json", ct).ConfigureAwait(false);
        if (mod is null)
            return new CachedMod { Available = false, FetchedAt = DateTimeOffset.UtcNow.ToString("O") };

        var cached = new CachedMod
        {
            Name = GetString(mod.Value, "name"),
            Author = GetString(mod.Value, "author"),
            Version = GetString(mod.Value, "version"),
            Available = mod.Value.TryGetProperty("available", out var available) && available.GetBoolean(),
            FetchedAt = DateTimeOffset.UtcNow.ToString("O"),
        };

        var files = await GetJsonAsync($"games/morrowind/mods/{nexusId}/files.json", ct).ConfigureAwait(false);
        if (files is not null && files.Value.TryGetProperty("files", out var fileArray))
        {
            foreach (var file in fileArray.EnumerateArray())
            {
                cached.Files.Add(new CachedFile
                {
                    FileId = file.GetProperty("file_id").GetInt64(),
                    Name = GetString(file, "name") ?? "",
                    FileName = GetString(file, "file_name"),
                    Version = GetString(file, "version"),
                    SizeBytes = file.TryGetProperty("size_in_bytes", out var size) &&
                                size.ValueKind == JsonValueKind.Number
                        ? size.GetInt64()
                        : 0,
                    Category = GetString(file, "category_name"),
                    UploadedTimestamp = file.TryGetProperty("uploaded_timestamp", out var ts)
                        ? ts.GetInt64()
                        : 0,
                });
            }
        }

        return cached;
    }

    private async Task<JsonElement?> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        await RespectBudgetAsync(ct).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + relativeUrl);
        request.Headers.Add("apikey", apiKey);
        request.Headers.Add("Application-Name", "MorrowindRemakeInstaller-Curation");
        request.Headers.Add("Application-Version", "0.1.0");

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        ReadBudgetHeaders(response);

        // 404/410: removed. 403: hidden/moderated (confirmed in the field —
        // one forbidden mod must not kill a 500-mod pass). All mean
        // "not available for automated download".
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound
            or System.Net.HttpStatusCode.Gone
            or System.Net.HttpStatusCode.Forbidden
            or System.Net.HttpStatusCode.Unauthorized)
            return null;
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            log?.Invoke("429 from Nexus — backing off 60s…");
            await Task.Delay(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
            return await GetJsonAsync(relativeUrl, ct).ConfigureAwait(false);
        }
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }

    private async Task RespectBudgetAsync(CancellationToken ct)
    {
        if (_dailyRemaining <= 1)
            throw new DailyBudgetExhaustedException();
        if (_hourlyRemaining <= 1 && _hourlyReset > DateTimeOffset.UtcNow)
        {
            var wait = _hourlyReset - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
            log?.Invoke($"Hourly Nexus budget exhausted — sleeping {wait.TotalMinutes:F0} min…");
            await Task.Delay(wait, ct).ConfigureAwait(false);
        }
    }

    private void ReadBudgetHeaders(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RL-Hourly-Remaining", out var hourly) &&
            int.TryParse(hourly.FirstOrDefault(), out var h))
            _hourlyRemaining = h;
        if (response.Headers.TryGetValues("X-RL-Daily-Remaining", out var daily) &&
            int.TryParse(daily.FirstOrDefault(), out var d))
            _dailyRemaining = d;
        if (response.Headers.TryGetValues("X-RL-Hourly-Reset", out var reset) &&
            DateTimeOffset.TryParse(reset.FirstOrDefault(), out var r))
            _hourlyReset = r;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
