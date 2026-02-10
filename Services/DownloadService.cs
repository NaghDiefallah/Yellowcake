using Octokit;
using Serilog;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;

namespace Yellowcake.Services;

public class DownloadService
{
    public record DownloadResult(MemoryStream Stream, string FinalUrl, string? SuggestedFileName);

    private readonly HttpClient _httpClient;
    private readonly GitHubClient _github;
    private readonly ProtonDriveService _protonDriveService;

    private static readonly string[] AllowedExtensions = { ".zip", ".7z", ".rar", ".dll" };
    private static readonly string[] JunkKeywords = { "sha", "sum", "sig", "hash", "readme", "license", "metadata", "json", "txt", "xml", "source", "debug", "symbols", "project" };

    public DownloadService(HttpClient httpClient, GitHubClient github)
    {
        _httpClient = httpClient;
        _github = github;
        _protonDriveService = new ProtonDriveService();
    }
    public async Task<DownloadResult> UniversalDownloadAsync(
        string url,
        string? expectedHash = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be empty", nameof(url));

        var finalUrl = await ResolveDirectLink(url, ct);
        Log.Debug("[DownloadService] Resolved URL '{Url}' -> '{FinalUrl}'", url, finalUrl);

        HttpResponseMessage response;
        if (ProtonDriveService.IsProtonDriveUrl(finalUrl))
        {
            Log.Information("[DownloadService] Detected Proton Drive URL, using specialized handler");
            response = await _protonDriveService.GetDownloadResponseAsync(finalUrl, null, ct);
        }
        else
        {
            response = await _httpClient.GetAsync(finalUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        response.EnsureSuccessStatusCode();

        string? suggested = null;
        try
        {
            var contentDisposition = response.Content.Headers.ContentDisposition;
            if (contentDisposition != null)
            {
                suggested = contentDisposition.FileNameStar ?? contentDisposition.FileName;
            }

            if (string.IsNullOrWhiteSpace(suggested))
            {
                try
                {
                    suggested = Path.GetFileName(new Uri(finalUrl).LocalPath);
                }
                catch { /* ignore */ }
            }

            if (!string.IsNullOrWhiteSpace(suggested))
            {
                suggested = suggested.Trim().Trim('"', '\'').Split('?')[0];
                suggested = SanitizeFileName(suggested);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[DownloadService] Failed to determine suggested filename for {Url}", finalUrl);
            suggested = null;
        }

        await using var input = await response.Content.ReadAsStreamAsync(ct);
        var output = new MemoryStream((int?)response.Content.Headers.ContentLength ?? 64 * 1024);

        const int BUF_SIZE = 131_072;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BUF_SIZE);
        try
        {
            long totalRead = 0;
            long? totalLength = response.Content.Headers.ContentLength;

            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, BUF_SIZE), ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                totalRead += read;

                if (totalLength.HasValue)
                    progress?.Report(Math.Min(100.0, totalRead * 100.0 / totalLength.Value));
            }

            if (!string.IsNullOrWhiteSpace(expectedHash) &&
                !await VerifyHashAsync(output, expectedHash, finalUrl))
            {
                throw new SecurityException($"Hash verification failed for {finalUrl}");
            }

            output.Position = 0;
            Log.Information("[DownloadService] Download completed: {FinalUrl} (bytes: {Len}) SuggestedFileName={Suggested}", finalUrl, output.Length, suggested);
            return new DownloadResult(output, finalUrl, suggested);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name ?? string.Empty;
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = Regex.Replace(name, @"\s+", " ").Trim();
        if (name.Length > 200) name = name.Substring(name.Length - 200);
        return name;
    }

    private async Task<string> ResolveDirectLink(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;

        var candidates = url.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(u => u.Trim())
                            .Where(u => !string.IsNullOrWhiteSpace(u));

        foreach (var candidate in candidates)
        {
            Log.Debug("[DownloadService] Evaluating candidate URL: {Candidate}", candidate);

            // GitHub blob -> raw
            if (candidate.Contains("github.com") && candidate.Contains("/blob/"))
            {
                var raw = candidate.Replace("/blob/", "/raw/");
                Log.Debug("[DownloadService] Converted github blob -> raw: {Raw}", raw);
                return raw;
            }

            // Already direct githubusercontent or similar
            if (candidate.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("[DownloadService] Using raw githubusercontent URL");
                return candidate;
            }

            // drive.usercontent and other direct googleusercontent hosts are already direct
            if (candidate.Contains("drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase) ||
                (candidate.Contains("googleusercontent.com", StringComparison.OrdinalIgnoreCase) && candidate.Contains("/download")))
            {
                Log.Debug("[DownloadService] Using googleusercontent direct download URL");
                return candidate;
            }

            // Handle drive.google.com interactive links by extracting id and attempting to produce a direct download link
            if (candidate.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("[DownloadService] Candidate is a drive.google.com link, attempting to resolve direct download");
                try
                {
                    // Extract file id from common patterns
                    var idMatch = Regex.Match(candidate, @"/file/d/([A-Za-z0-9_\-]+)");
                    if (!idMatch.Success)
                        idMatch = Regex.Match(candidate, @"[?&]id=([A-Za-z0-9_\-]+)");

                    if (!idMatch.Success)
                    {
                        Log.Debug("[DownloadService] Could not parse Drive ID from {Candidate}", candidate);
                        continue;
                    }

                    string id = idMatch.Groups[1].Value;
                    string ucUrl = $"https://drive.google.com/uc?export=download&id={id}";

                    using var headResp = await _httpClient.GetAsync(ucUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!headResp.IsSuccessStatusCode)
                    {
                        Log.Debug("[DownloadService] HEAD to {UcUrl} returned {Status}", ucUrl, headResp.StatusCode);
                        continue;
                    }

                    var mediaType = headResp.Content.Headers.ContentType?.MediaType ?? string.Empty;
                    if (!mediaType.Contains("text", StringComparison.OrdinalIgnoreCase) &&
                        !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Debug("[DownloadService] Drive uc endpoint returned binary content for id={Id}", id);
                        return ucUrl;
                    }

                    var html = await headResp.Content.ReadAsStringAsync(ct);

                    var confirmMatch = Regex.Match(html, @"confirm=([0-9A-Za-z_\-]+)&id=" + Regex.Escape(id));
                    if (!confirmMatch.Success)
                    {
                        confirmMatch = Regex.Match(html, @"uc\?export=download&amp;confirm=([0-9A-Za-z_\-]+)&amp;id=" + Regex.Escape(id));
                    }

                    if (confirmMatch.Success)
                    {
                        string token = confirmMatch.Groups[1].Value;
                        string confirmed = $"https://drive.google.com/uc?export=download&confirm={token}&id={id}";
                        Log.Debug("[DownloadService] Found Drive confirm token for id={Id}", id);
                        return confirmed;
                    }

                    var hrefMatch = Regex.Match(html, @"href\s*=\s*[""'](https?:\/\/[^""']+confirm=[0-9A-Za-z_\-]+[^""']+)[""']", RegexOptions.IgnoreCase);
                    if (hrefMatch.Success)
                    {
                        var link = WebUtility.HtmlDecode(hrefMatch.Groups[1].Value);
                        Log.Debug("[DownloadService] Found Drive confirm href for id={Id}", id);
                        return link;
                    }

                    Log.Debug("[DownloadService] No confirm token found; falling back to ucUrl for id={Id}", id);
                    return ucUrl;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Drive link handling failed for {Url}", candidate);
                    continue;
                }
            }

            // If candidate looks like a direct file link (zip, dll, raw, releases/download etc.) accept it
            if (candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                candidate.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("/raw/", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("/releases/download/", StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("mediafire.com", StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("[DownloadService] Candidate appears to be direct file link");
                return candidate;
            }

            // Default: return the first reasonable candidate
            Log.Debug("[DownloadService] Accepting candidate as-is");
            return candidate;
        }

        // Last resort: return original url
        Log.Debug("[DownloadService] No candidate resolved; returning original URL");
        return url;
    }

    private async Task<bool> VerifyHashAsync(MemoryStream stream, string? expectedHash, string url)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            if (url.Contains("github.com"))
                expectedHash = await TryFetchHashFromGitHubMetadata(url);

            if (string.IsNullOrWhiteSpace(expectedHash))
            {
                Log.Warning("No hash provided for {Url}. Skipping verification.", url);
                return true;
            }
        }

        stream.Position = 0;
        string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        string cleanExpected = expectedHash.Split(':').Last().Trim();

        bool isValid = actualHash.Equals(cleanExpected, StringComparison.OrdinalIgnoreCase);

        if (isValid) Log.Information("Hash verified: {Url}", url);
        else Log.Error("Hash mismatch! URL: {Url} | Expected: {Exp} | Actual: {Act}", url, cleanExpected, actualHash);

        return isValid;
    }

    private async Task<string?> TryFetchHashFromGitHubMetadata(string url)
    {
        try
        {
            var uri = new Uri(url);
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;

            var release = await _github.Repository.Release.GetLatest(parts[0], parts[1]);
            if (string.IsNullOrWhiteSpace(release.Body)) return null;

            var fileName = Path.GetFileName(uri.LocalPath);
            var pattern = $@"""{Regex.Escape(fileName)}.*?([A-Fa-f0-9]{{64}})""";
            var match = Regex.Match(release.Body, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            return match.Success ? match.Groups[1].Value : Regex.Match(release.Body, @"\b[A-Fa-f0-9]{64}\b").Value;
        }
        catch { return null; }
    }

    public async Task<(string url, string tag)> GetLatestReleaseInfo(string owner, string repo)
    {
        await CheckRateLimit();

        try
        {
            var latest = await _github.Repository.Release.GetLatest(owner, repo);
            return SelectBestAsset(latest, repo);
        }
        catch (NotFoundException)
        {
            var releases = await _github.Repository.Release.GetAll(owner, repo);
            var best = releases.FirstOrDefault(r => !r.Prerelease) ?? releases.FirstOrDefault();
            if (best != null) return SelectBestAsset(best, repo);
        }

        return await GetBranchFallback(owner, repo);
    }

    private (string url, string tag) SelectBestAsset(Release release, string repoName)
    {
        string tag = ParseVersion(release.TagName);

        if (release.Assets?.Count > 0)
        {
            var asset = release.Assets
                .Where(a => !JunkKeywords.Any(k => a.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(a => a.Name.Contains(repoName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(a => AllowedExtensions.Any(ext => a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(a => a.DownloadCount)
                .FirstOrDefault();

            if (asset != null) return (asset.BrowserDownloadUrl, tag);
        }

        return (release.ZipballUrl ?? $"https://github.com/{repoName}/archive/refs/tags/{release.TagName}.zip", tag);
    }

    private string ParseVersion(string input)
    {
        var match = Regex.Match(input, @"(\d+\.)*(\d+)");
        return match.Success ? match.Value.Trim('.') : input;
    }

    private async Task CheckRateLimit()
    {
        try
        {
            var limits = await _github.RateLimit.GetRateLimits();
            if (limits.Resources.Core.Remaining <= 0)
            {
                throw new InvalidOperationException($"GitHub API Rate Limit Exhausted. Resets at {limits.Resources.Core.Reset.ToLocalTime():t}");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException) { }
    }

    private async Task<(string url, string tag)> GetBranchFallback(string owner, string repo)
    {
        try
        {
            var repository = await _github.Repository.Get(owner, repo);
            string branch = repository.DefaultBranch ?? "main";
            return ($"https://github.com/{owner}/{repo}/archive/refs/heads/{branch}.zip", branch);
        }
        catch { return ($"https://github.com/{owner}/{repo}/archive/refs/heads/main.zip", "main"); }
    }
}