using Avalonia.Media.Imaging;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Yellowcake.Services;

public class EnhancedThumbnailCache
{
    private readonly string _cacheDirectory;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, Bitmap> _memoryCache = new();
    private readonly ConcurrentDictionary<string, Task<Bitmap>> _pendingLoads = new();
    private readonly SemaphoreSlim _downloadSemaphore = new(3, 3); // Max 3 concurrent downloads
    private const int MaxMemoryCacheSize = 50;
    private const long MaxDiskCacheSize = 100 * 1024 * 1024; // 100MB

    private static readonly byte[] PlaceholderPng = Convert.FromBase64String(
        "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"); // 1x1 transparent PNG

    public EnhancedThumbnailCache(HttpClient http)
    {
        _http = http;
        _cacheDirectory = Path.Combine(AppContext.BaseDirectory, "cache", "thumbnails");
        Directory.CreateDirectory(_cacheDirectory);
        
        _ = Task.Run(CleanOldCacheAsync);
    }

    public async Task<Bitmap?> GetOrDownloadAsync(string url, bool lowQuality = false)
    {
        if (string.IsNullOrEmpty(url)) return GetPlaceholderImage();

        var cacheKey = GetCacheKey(url);
        
        if (_memoryCache.TryGetValue(cacheKey, out var cached))
            return cached;

        if (_pendingLoads.TryGetValue(cacheKey, out var pending))
            return await pending;

        var loadTask = LoadImageAsync(url, cacheKey, lowQuality);
        _pendingLoads.TryAdd(cacheKey, loadTask);

        try
        {
            return await loadTask;
        }
        finally
        {
            _pendingLoads.TryRemove(cacheKey, out _);
        }
    }

    private async Task<Bitmap?> LoadImageAsync(string url, string cacheKey, bool lowQuality)
    {
        try
        {
            var cachePath = GetCachePath(cacheKey);
            if (File.Exists(cachePath))
            {
                var bitmap = await LoadFromDiskAsync(cachePath);
                if (bitmap != null)
                {
                    AddToMemoryCache(cacheKey, bitmap);
                    return bitmap;
                }
            }

            await _downloadSemaphore.WaitAsync();
            try
            {
                var bitmap = await DownloadImageAsync(url, lowQuality);
                if (bitmap != null)
                {
                    AddToMemoryCache(cacheKey, bitmap);
                    await SaveToDiskAsync(cachePath, bitmap);
                }
                return bitmap;
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load thumbnail: {Url}", url);
            return GetPlaceholderImage();
        }
    }

    private async Task<Bitmap?> DownloadImageAsync(string url, bool lowQuality)
    {
        try
        {
            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            
            if (lowQuality && (bitmap.PixelSize.Width > 400 || bitmap.PixelSize.Height > 400))
            {
                return ResizeBitmap(bitmap, 400, 400);
            }
            
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download image from {Url}", url);
            return null;
        }
    }

    private Bitmap? ResizeBitmap(Bitmap source, int maxWidth, int maxHeight)
    {
        try
        {
            var ratio = Math.Min((double)maxWidth / source.PixelSize.Width, (double)maxHeight / source.PixelSize.Height);
            var newWidth = (int)(source.PixelSize.Width * ratio);
            var newHeight = (int)(source.PixelSize.Height * ratio);

            return source.CreateScaledBitmap(new Avalonia.PixelSize(newWidth, newHeight));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resize bitmap");
            return source;
        }
    }

    private async Task<Bitmap?> LoadFromDiskAsync(string path)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load cached image: {Path}", path);
            return null;
        }
    }

    private async Task SaveToDiskAsync(string path, Bitmap bitmap)
    {
        try
        {
            await using var stream = File.Create(path);
            bitmap.Save(stream);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save image to cache: {Path}", path);
        }
    }

    private void AddToMemoryCache(string key, Bitmap bitmap)
    {
        if (_memoryCache.Count >= MaxMemoryCacheSize)
        {
            var toRemove = _memoryCache.Keys.Take(_memoryCache.Count - MaxMemoryCacheSize + 1).ToList();
            foreach (var k in toRemove)
            {
                if (_memoryCache.TryRemove(k, out var old))
                {
                    old?.Dispose();
                }
            }
        }

        _memoryCache.TryAdd(key, bitmap);
    }

    private string GetCacheKey(string url)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(url));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private string GetCachePath(string cacheKey) => Path.Combine(_cacheDirectory, $"{cacheKey}.png");

    private Bitmap GetPlaceholderImage()
    {
        try
        {
            var assembly = typeof(EnhancedThumbnailCache).Assembly;
            var resourceName = "Yellowcake.Assets.placeholder.png";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
                return new Bitmap(stream);

            return new Bitmap(new MemoryStream(PlaceholderPng));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load placeholder image");
            return new Bitmap(new MemoryStream(PlaceholderPng));
        }
    }

    private async Task CleanOldCacheAsync()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory)) return;

            var files = Directory.GetFiles(_cacheDirectory)
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.LastAccessTime)
                .ToList();

            long totalSize = files.Sum(f => f.Length);

            while (totalSize > MaxDiskCacheSize && files.Any())
            {
                var oldest = files.First();
                try
                {
                    oldest.Delete();
                    totalSize -= oldest.Length;
                    files.Remove(oldest);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to delete cache file: {Path}", oldest.FullName);
                    break;
                }
            }

            Log.Information("Cache cleanup complete. Size: {Size:N0} bytes, Files: {Count}", totalSize, files.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clean cache");
        }
    }

    public void ClearCache()
    {
        try
        {
            foreach (var bitmap in _memoryCache.Values)
            {
                bitmap?.Dispose();
            }
            _memoryCache.Clear();

            if (Directory.Exists(_cacheDirectory))
            {
                foreach (var file in Directory.GetFiles(_cacheDirectory))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to delete cache file: {Path}", file);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clear cache");
        }
    }

    public void Dispose()
    {
        foreach (var bitmap in _memoryCache.Values)
        {
            bitmap?.Dispose();
        }
        _memoryCache.Clear();
    }
}