using Octokit;
using Serilog;
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Helpers;

namespace Yellowcake.Services;

public class DownloadService
{
    private readonly HttpClient _httpClient;
    private readonly GitHubClient _github;
    private static readonly string[] AllowedExtensions = { ".zip", ".7z", ".rar", ".dll" };
    private static readonly string[] JunkKeywords = { "sha", "sum", "sig", "hash", "readme", "license", "metadata", "json", "txt", "xml", "source", "debug", "symbols", "project" };

    public DownloadService(HttpClient httpClient, GitHubClient github)
    {
        _httpClient = httpClient;
        _github = github;
    }

    public async Task<MemoryStream> DownloadWithProgress(string url, string? expectedHash, IProgress<double>? progressReport, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var contentStream = await response.Content.ReadAsStreamAsync(ct);

            var ms = new MemoryStream();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(131072);

            try
            {
                int bytesRead;
                long totalRead = 0;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await ms.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progress = Math.Round((double)totalRead / totalBytes * 100, 1);
                        progressReport?.Report(Math.Clamp(progress, 0, 100));
                    }
                }

                if (!await VerifyHashAsync(ms, expectedHash, url))
                {
                    throw new SecurityException("File integrity check failed: Hash mismatch.");
                }

                ms.Position = 0;
                return ms;
            }
            catch
            {
                await ms.DisposeAsync();
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Download cancelled: {Url}", url);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Download failed for {Url}", url);
            NotificationService.Instance.Error($"Download Failed: {ex.Message}");
            throw;
        }
    }

    private async Task<bool> VerifyHashAsync(MemoryStream stream, string? expectedHash, string url)
    {
        stream.Position = 0;
        string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream));

        string? targetHash = expectedHash;

        if (string.IsNullOrWhiteSpace(targetHash) && url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            targetHash = await FetchGitHubChecksumAsync(url);
        }

        if (string.IsNullOrWhiteSpace(targetHash))
        {
            Log.Warning("No hash authority found for {Url}. Skipping verification.", url);
            return true;
        }

        var cleanExpected = targetHash.Split(':').Last().Trim();
        var isValid = actualHash.Equals(cleanExpected, StringComparison.OrdinalIgnoreCase);

        if (isValid)
        {
            Log.Information("Hash verified for {Url}", url);
        }
        else
        {
            Log.Error("Integrity failure for {Url}. Expected: {Expected}, Actual: {Actual}", url, cleanExpected, actualHash);
        }

        return isValid;
    }

    private async Task<string?> FetchGitHubChecksumAsync(string downloadUrl)
    {
        try
        {
            var uri = new Uri(downloadUrl);
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;

            string owner = parts[0];
            string repo = parts[1];
            string fileName = Path.GetFileName(uri.LocalPath);

            // Try Release Body parsing via Octokit
            var release = await _github.Repository.Release.GetLatest(owner, repo);
            if (!string.IsNullOrWhiteSpace(release.Body))
            {
                // Look for a 64-char hex string near the filename in the release notes
                var pattern = $@"{Regex.Escape(fileName)}.*?([A-Fa-f0-9]{{64}})";
                var match = Regex.Match(release.Body, pattern, RegexOptions.Singleline);

                if (match.Success) return match.Groups[1].Value;

                // Fallback: search for any standalone SHA256 hash in the body
                var genericMatch = Regex.Match(release.Body, @"\b[A-Fa-f0-9]{64}\b");
                if (genericMatch.Success) return genericMatch.Value;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<(string url, string tag)> GetLatestReleaseInfo(string owner, string repo)
    {
        await CheckRateLimit();

        var cleanOwner = owner?.Trim() ?? throw new ArgumentNullException(nameof(owner));
        var cleanRepo = repo?.Trim().Replace(".git", "", StringComparison.OrdinalIgnoreCase) ?? throw new ArgumentNullException(nameof(repo));

        try
        {
            var latest = await _github.Repository.Release.GetLatest(cleanOwner, cleanRepo);
            return ProcessRelease(latest, cleanRepo);
        }
        catch (NotFoundException)
        {
            Log.Debug("No 'Latest' release tag found for {Repo}. Searching all releases...", cleanRepo);
        }
        catch (Exception ex) when (ex is not ForbiddenException)
        {
            Log.Warning("Primary API lookup failed for {Repo}: {Message}", cleanRepo, ex.Message);
        }

        try
        {
            var releases = await _github.Repository.Release.GetAll(cleanOwner, cleanRepo);
            var bestRelease = releases.FirstOrDefault(r => !r.Prerelease) ?? releases.FirstOrDefault();

            if (bestRelease != null)
                return ProcessRelease(bestRelease, cleanRepo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to retrieve any releases for {Repo}.", cleanRepo);
        }

        return await GetBranchFallback(cleanOwner, cleanRepo);
    }

    private (string url, string tag) ProcessRelease(Release release, string repoName)
    {
        string rawTag = release.TagName ?? "1.0.0";
        string cleanTag = ParseVersion(rawTag);

        if (release.Assets?.Count > 0)
        {
            var priorityExtensions = new[] { ".zip", ".7z", ".rar" };

            var asset = release.Assets
                .Where(a => !JunkKeywords.Any(k => a.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(a => a.Name.Contains(repoName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(a => a.Name.StartsWith(repoName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(a => priorityExtensions.Any(ext => a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(a => a.DownloadCount)
                .FirstOrDefault();

            if (asset != null && AllowedExtensions.Any(ext => asset.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Information("Selected Asset: {Name} ({Tag})", asset.Name, cleanTag);
                return (asset.BrowserDownloadUrl, cleanTag);
            }
        }

        string fallback = release.ZipballUrl ?? $"https://github.com/{repoName}/archive/refs/tags/{rawTag}.zip";
        Log.Information("Falling back to source archive for {Repo} ({Tag})", repoName, cleanTag);
        return (fallback, cleanTag);
    }

    private string ParseVersion(string input)
    {
        var match = Regex.Match(input, @"(\d+\.)*(\d+)");
        if (match.Success) return match.Value.Trim('.');

        string[] noise = { "BepInEx", "Release", "Tag", "Version", "Mod", "build", "stable", "v" };
        string working = input;
        foreach (var word in noise)
            working = Regex.Replace(working, word, "", RegexOptions.IgnoreCase);

        return working.Trim('-', '_', '.', ' ') is string s && !string.IsNullOrWhiteSpace(s) ? s : input;
    }

    private async Task CheckRateLimit()
    {
        try
        {
            var limits = await _github.Miscellaneous.GetRateLimits();
            var core = limits.Resources.Core;

            if (core.Remaining <= 0)
            {
                var reset = core.Reset.ToLocalTime().ToString("t");
                throw new InvalidOperationException($"GitHub API limit reached. Resets at {reset}.");
            }

            if (core.Remaining < 10)
                Log.Warning("GitHub API rate limit low: {Remaining} remaining.", core.Remaining);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            Log.Debug("Rate limit check skipped: {Message}", ex.Message);
        }
    }

    private async Task<(string url, string tag)> GetBranchFallback(string owner, string repo)
    {
        try
        {
            var repository = await _github.Repository.Get(owner, repo);
            string branch = repository.DefaultBranch ?? "main";
            return ($"https://github.com/{owner}/{repo}/archive/refs/heads/{branch}.zip", branch);
        }
        catch
        {
            return ($"https://github.com/{owner}/{repo}/archive/refs/heads/main.zip", "main");
        }
    }
}