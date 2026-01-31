using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Yellowcake.Services;

public class BepInExService : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string ApiUrl = "https://api.github.com/repos/BepInEx/BepInEx/releases";

    public BepInExService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Yellowcake-Mod-Manager");
    }

    public async Task<List<string>> GetVersionListAsync()
    {
        try
        {
            var releases = await _httpClient.GetFromJsonAsync<List<GitHubRelease>>(ApiUrl);
            if (releases == null) return new List<string>();

            return releases
                .Select(r => r.TagName)
                .OrderByDescending(tag =>
                {
                    var cleanTag = tag.StartsWith("v") ? tag.Substring(1) : tag;
                    var versionPart = cleanTag.Split('-')[0];

                    if (Version.TryParse(versionPart, out var v))
                        return v;

                    return new Version(0, 0);
                })
                .ThenByDescending(tag => tag)
                .ToList();
        }
        catch (HttpRequestException)
        {
            return new List<string>();
        }
    }

    public async Task InstallVersionAsync(string version, string gamePath, Action<double> progressCallback)
    {
        var releases = await _httpClient.GetFromJsonAsync<List<GitHubRelease>>(ApiUrl);
        var release = releases?.FirstOrDefault(r => r.TagName == version)
                      ?? throw new InvalidOperationException($"Version {version} not found.");

        var asset = release.Assets.FirstOrDefault(a => a.Name.Contains("x64") && a.Name.EndsWith(".zip"))
                    ?? throw new FileNotFoundException("No compatible x64 ZIP found.");

        var gameDir = Path.GetDirectoryName(gamePath)
                      ?? throw new DirectoryNotFoundException("Invalid game directory.");

        var tempFile = Path.Combine(Path.GetTempPath(), $"bepinex_{Guid.NewGuid()}.zip");

        try
        {
            using (var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);
                using var downloadStream = await response.Content.ReadAsStreamAsync();

                var buffer = new byte[81920];
                var totalRead = 0L;
                int bytesRead;

                while ((bytesRead = await downloadStream.ReadAsync(buffer)) != 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    if (totalBytes != -1)
                        progressCallback((double)totalRead / totalBytes * 100);
                }
            }

            ZipFile.ExtractToDirectory(tempFile, gameDir, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    public void Uninstall(string gamePath)
    {
        var gameDir = Path.GetDirectoryName(gamePath);
        if (string.IsNullOrEmpty(gameDir)) return;

        var targets = new[] { "BepInEx", "doorstop_config.ini", "winhttp.dll", "changelog.txt" };

        foreach (var target in targets)
        {
            var path = Path.Combine(gameDir, target);
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

public record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("assets")] List<GitHubAsset> Assets
);

public record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl
);