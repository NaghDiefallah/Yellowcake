using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Yellowcake.Models;

namespace Yellowcake.Services;

public class ManifestService
{
    private readonly HttpClient _httpClient;
    private const string PrimaryUrl = "https://kopterbuzz.github.io/NOModManifestTesting/manifest/manifest.json";
    private const string SecondaryUrl = "https://gist.githubusercontent.com/NaghDiefallah/82544b5e011d78924b0ff7678e4180aa/raw";

    public bool UseSecondaryManifest { get; set; } = false;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public ManifestService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<List<Mod>> FetchRemoteManifest()
    {
        string activeSource = UseSecondaryManifest ? "Secondary" : "Primary";
        string targetUrl = UseSecondaryManifest ? SecondaryUrl : PrimaryUrl;

        Log.Information("Fetching {Source} manifest...", activeSource);

        var mods = await FetchFromSource(targetUrl, activeSource);

        foreach (var mod in mods)
        {
            mod.FinalizeFromManifest();
        }

        Log.Information("Manifest sync complete. Loaded {Count} mods from {Source}.", mods.Count, activeSource);
        return mods;
    }

    private async Task<List<Mod>> FetchFromSource(string url, string sourceName)
    {
        try
        {
            var requestUrl = $"{url}?t={DateTime.UtcNow.Ticks}";
            var dtos = await _httpClient.GetFromJsonAsync<List<ModDto>>(requestUrl, JsonOptions);

            return dtos?.Select(d => MapDtoToMod(d, sourceName)).ToList() ?? [];
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to fetch {Source} manifest from {Url}", sourceName, url);
            return [];
        }
    }

    private Mod MapDtoToMod(ModDto d, string sourceName)
    {
        var artifact = d.Artifacts?.FirstOrDefault();
        var id = d.Id ?? Guid.NewGuid().ToString();

        return new Mod
        {
            Id = id,
            Name = d.DisplayName ?? id,
            Description = d.Description ?? string.Empty,
            Authors = d.Authors ?? ["Unknown"],
            GitHubUrl = d.InfoUrl,
            Source = sourceName,
            Version = artifact?.Version ?? "0.0.0",
            DownloadUrl = artifact?.DownloadUrl,
            Category = artifact?.Type ?? "plugin",
            ExpectedHash = artifact?.Hash,
            Tags = d.Tags ?? []
        };
    }

    private class ModDto
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string? InfoUrl { get; set; }
        public List<string>? Authors { get; set; }
        public List<string>? Tags { get; set; }
        public List<ArtifactDto>? Artifacts { get; set; }
    }

    private class ArtifactDto
    {
        public string? FileName { get; set; }
        public string? Version { get; set; }
        public string? Type { get; set; }
        public string? DownloadUrl { get; set; }
        public string? Hash { get; set; }
    }
}