using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;

namespace Yellowcake.Services;

public class ManifestService
{
    private readonly HttpClient _httpClient;
    private const string GistUrl = "https://gist.github.com/NaghDiefallah/82544b5e011d78924b0ff7678e4180aa/raw";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public ManifestService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<List<Mod>> FetchRemoteManifest()
    {
        Log.Information("Fetching mod manifest from Gist.");

        try
        {
            string requestUrl = $"{GistUrl}?t={DateTime.UtcNow.Ticks}";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var response = await _httpClient.GetAsync(requestUrl, cts.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var mods = await JsonSerializer.DeserializeAsync<List<Mod>>(stream, JsonOptions, cts.Token);

            if (mods == null) return [];

            var validMods = mods.Where(m => !string.IsNullOrWhiteSpace(m.Id)).ToList();

            foreach (var mod in validMods)
            {
                mod.FinalizeFromManifest();
                CategorizeMod(mod);
            }

            Log.Information("Successfully loaded {Count} mods.", validMods.Count);
            return validMods;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to retrieve manifest.");
            NotificationService.Instance.Error("Failed to sync with the remote mod manifest.");
            return [];
        }
    }

    private void CategorizeMod(Mod mod)
    {
        string name = mod.Name ?? mod.Id ?? "Unknown";
        string desc = (mod.Description ?? string.Empty).ToLowerInvariant();
        string cat = (mod.Category ?? "Plugin").ToLowerInvariant();

        if (name.Contains("WSOYappinator", StringComparison.OrdinalIgnoreCase) ||
            mod.Id.Contains("WSOYappinator", StringComparison.OrdinalIgnoreCase))
        {
            mod.Category = "Plugin";
            mod.IsVoicePack = false;
            mod.IsLivery = false;
        }
        else if (cat == "plugin")
        {
            mod.Category = "Plugin";
            mod.IsVoicePack = false;
            mod.IsLivery = false;
        }
        else if (cat == "audio" || cat == "voice pack" || desc.Contains("voice pack") || desc.Contains("sound pack"))
        {
            mod.Category = "Audio";
            mod.IsVoicePack = true;
            mod.IsLivery = false;
        }
        else if (cat == "visual" || cat == "livery" || desc.Contains("aircraft skin") || desc.Contains("paint job"))
        {
            mod.Category = "Visual";
            mod.IsLivery = true;
            mod.IsVoicePack = false;
        }
        else
        {
            mod.Category = cat.Length > 0 ? char.ToUpper(cat[0]) + cat[1..] : "Plugin";
            mod.IsVoicePack = false;
            mod.IsLivery = false;
        }

        if (mod.Addons != null)
        {
            foreach (var addon in mod.Addons)
            {
                addon.IsVoicePack = string.Equals(addon.Category, "Audio", StringComparison.OrdinalIgnoreCase) ||
                                   (addon.Name?.Contains("voice", StringComparison.OrdinalIgnoreCase) ?? false);
            }
        }
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}