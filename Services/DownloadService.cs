using Octokit;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Helpers;

namespace Yellowcake.Services;

public class DownloadService
{
    private readonly HttpClient _httpClient;
    private readonly GitHubClient _github;
    private static readonly string[] AllowedExtensions = [".zip", ".7z", ".rar", ".dll"];

    public DownloadService(HttpClient httpClient, GitHubClient github)
    {
        _httpClient = httpClient;
        _github = github;
    }

    public async Task<MemoryStream> DownloadWithProgress(string url, Action<double>? progressReport, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var contentStream = await response.Content.ReadAsStreamAsync(ct);

            var ms = new MemoryStream();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);

            try
            {
                long totalRead = 0L;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await ms.WriteAsync(buffer.AsMemory(0, read), ct);
                    totalRead += read;

                    if (totalBytes > 0)
                    {
                        progressReport?.Invoke(Math.Round((double)totalRead / totalBytes * 100, 1));
                    }
                }

                ms.Position = 0;
                return ms;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Download cancelled for {Url}", url);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Download failed for {Url}", url);
            NotificationService.Instance.Error("Download failed. Check your connection.");
            throw;
        }
    }

    public async Task<(string url, string tag)> GetLatestReleaseInfo(string owner, string repo)
    {
        await CheckRateLimit();

        string cleanOwner = owner.Trim();
        string cleanRepo = repo.Trim();

        try
        {
            var releases = await _github.Repository.Release.GetAll(cleanOwner, cleanRepo);

            if (releases != null && releases.Count > 0)
            {
                // Prioritize stable releases, fall back to pre-releases
                var targetRelease = releases.FirstOrDefault(r => !r.Prerelease) ?? releases[0];

                var asset = targetRelease.Assets.FirstOrDefault(a =>
                    AllowedExtensions.Any(ext => a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

                if (asset != null)
                {
                    Log.Information("Found asset: {Name} for {Repo} ({Tag})", asset.Name, cleanRepo, targetRelease.TagName);
                    return (asset.BrowserDownloadUrl, targetRelease.TagName);
                }

                Log.Warning("No binary assets found for {Repo} {Tag}, using source zip.", cleanRepo, targetRelease.TagName);
                return (targetRelease.ZipballUrl, targetRelease.TagName);
            }
        }
        catch (NotFoundException)
        {
            Log.Warning("Repository or releases not found for {Owner}/{Repo}", cleanOwner, cleanRepo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error querying GitHub for {Repo}", cleanRepo);
        }

        return await GetBranchFallback(cleanOwner, cleanRepo);
    }

    private async Task CheckRateLimit()
    {
        try
        {
            var limits = await _github.Miscellaneous.GetRateLimits();
            var core = limits.Resources.Core;

            if (core.Remaining == 0)
            {
                string message = $"GitHub rate limit exceeded. Resets at {core.Reset.ToLocalTime():t}";
                NotificationService.Instance.Error(message);
                throw new InvalidOperationException(message);
            }

            if (core.Remaining < 10)
            {
                Log.Warning("GitHub API rate limit low: {Remaining} left", core.Remaining);
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            Log.Debug("Rate limit check bypassed: {Message}", ex.Message);
        }
    }

    private async Task<(string url, string tag)> GetBranchFallback(string owner, string repo)
    {
        try
        {
            var repository = await _github.Repository.Get(owner, repo);
            string branch = repository.DefaultBranch ?? "main";
            Log.Information("Falling back to branch: {Branch} for {Repo}", branch, repo);
            return ($"https://github.com/{owner}/{repo}/archive/refs/heads/{branch}.zip", branch);
        }
        catch
        {
            return ($"https://github.com/{owner}/{repo}/archive/refs/heads/main.zip", "main");
        }
    }
}