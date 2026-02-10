using Serilog;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Yellowcake.Services;

public class ProtonDriveService
{
    private static readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    private static readonly Regex ProtonUrlRegex = new(
        @"https://drive\.proton\.me/urls/([A-Z0-9]+)#([A-Za-z0-9_-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static bool IsProtonDriveUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return ProtonUrlRegex.IsMatch(url);
    }

    public static (string shareId, string password)? ParseProtonUrl(string url)
    {
        var match = ProtonUrlRegex.Match(url);
        if (!match.Success) return null;

        return (match.Groups[1].Value, match.Groups[2].Value);
    }

    public async Task<HttpResponseMessage> GetDownloadResponseAsync(
        string url, 
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        var parsed = ParseProtonUrl(url);
        if (parsed == null)
        {
            throw new ArgumentException("Invalid Proton Drive URL", nameof(url));
        }

        var (shareId, password) = parsed.Value;

        try
        {
            Log.Information("Downloading from Proton Drive: {ShareId}", shareId);

            var apiUrl = $"https://drive.proton.me/api/shares/urls/{shareId}";
            
            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("Accept", "application/json");
            
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Proton Drive API returned {StatusCode}", response.StatusCode);
                throw new HttpRequestException($"Proton Drive returned {response.StatusCode}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync(ct);
            Log.Debug("Proton Drive API response: {Response}", jsonResponse);

            var downloadUrl = ExtractDownloadUrl(jsonResponse);
            if (string.IsNullOrEmpty(downloadUrl))
            {
                Log.Warning("Could not extract download URL from Proton Drive response");
                throw new InvalidOperationException("Failed to get download URL from Proton Drive");
            }

            Log.Information("Got Proton Drive download URL: {Url}", downloadUrl);

            var downloadRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            var downloadResponse = await _httpClient.SendAsync(
                downloadRequest, 
                HttpCompletionOption.ResponseHeadersRead, 
                ct
            );

            if (!downloadResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Download failed with status {downloadResponse.StatusCode}");
            }

            return downloadResponse;
        }
        catch (OperationCanceledException)
        {
            Log.Information("Proton Drive download cancelled");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download from Proton Drive: {Url}", url);
            throw;
        }
    }

    private string? ExtractDownloadUrl(string jsonResponse)
    {
        try
        {
            var linkIdxStart = jsonResponse.IndexOf("\"LinkID\":", StringComparison.Ordinal);
            if (linkIdxStart == -1) return null;

            var linkIdStart = jsonResponse.IndexOf("\"", linkIdxStart + 10, StringComparison.Ordinal);
            if (linkIdStart == -1) return null;

            var linkIdEnd = jsonResponse.IndexOf("\"", linkIdStart + 1, StringComparison.Ordinal);
            if (linkIdEnd == -1) return null;

            var linkId = jsonResponse.Substring(linkIdStart + 1, linkIdEnd - linkIdStart - 1);

            var shareIdxStart = jsonResponse.IndexOf("\"ShareID\":", StringComparison.Ordinal);
            if (shareIdxStart == -1) return null;

            var shareIdStart = jsonResponse.IndexOf("\"", shareIdxStart + 11, StringComparison.Ordinal);
            if (shareIdStart == -1) return null;

            var shareIdEnd = jsonResponse.IndexOf("\"", shareIdStart + 1, StringComparison.Ordinal);
            if (shareIdEnd == -1) return null;

            var shareId = jsonResponse.Substring(shareIdStart + 1, shareIdEnd - shareIdStart - 1);

            return $"https://drive.proton.me/api/shares/urls/{shareId}/files/{linkId}";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse Proton Drive JSON response");
            return null;
        }
    }

    public async Task<long?> GetFileSizeAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var response = await GetDownloadResponseAsync(url, null, ct);
            return response.Content.Headers.ContentLength;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to get file size from Proton Drive");
            return null;
        }
    }
}