using Avalonia.Media.Imaging;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Yellowcake.Services;

public class ThumbnailCache
{
    private static readonly Lazy<ThumbnailCache> _lazy = new(() => new ThumbnailCache());
    public static ThumbnailCache Instance => _lazy.Value;

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, Bitmap?> _cache = new();
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _pendingLoads = new();
    private readonly string _cachePath;
    private readonly SemaphoreSlim _diskLock = new(1, 1);

    private ThumbnailCache()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Yellowcake/1.0");

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cachePath = Path.Combine(appData, "Yellowcake", "thumbnails");
        if (!Directory.Exists(_cachePath)) Directory.CreateDirectory(_cachePath);
    }

    public async Task<Bitmap?> GetThumbnailAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (_cache.TryGetValue(url, out var cached))
            return cached;

        if (_pendingLoads.TryGetValue(url, out var pending))
            return await pending;

        var loadTask = LoadThumbnailAsync(url, cancellationToken);
        _pendingLoads.TryAdd(url, loadTask);

        try
        {
            var bitmap = await loadTask;
            _cache.TryAdd(url, bitmap);
            return bitmap;
        }
        finally
        {
            _pendingLoads.TryRemove(url, out _);
        }
    }

    private async Task<Bitmap?> LoadThumbnailAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
            var diskPath = Path.Combine(_cachePath, $"{hash}.jpg");

            if (File.Exists(diskPath))
            {
                await using var fileStream = File.OpenRead(diskPath);
                return new Bitmap(fileStream);
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var bitmap = new Bitmap(stream);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _diskLock.WaitAsync(cancellationToken);
                    try
                    {
                        bitmap.Save(diskPath);
                    }
                    finally
                    {
                        _diskLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to cache thumbnail to disk: {Url}", url);
                }
            }, cancellationToken);

            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to load thumbnail: {Url}", url);
            return null;
        }
    }

    public void ClearCache()
    {
        _cache.Clear();
        try
        {
            if (Directory.Exists(_cachePath))
            {
                Directory.Delete(_cachePath, true);
                Directory.CreateDirectory(_cachePath);
            }
            Log.Information("Thumbnail cache cleared");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to clear thumbnail cache directory");
        }
    }
}