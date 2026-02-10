using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Models;

namespace Yellowcake.Services;

public class ManifestService
{
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(10);
    private readonly string _cachePath;

    public string TargetUrl { get; set; } = string.Empty;

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Include
    };

    public ManifestService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string cacheDir = Path.Combine(appData, "Yellowcake", "cache");
        
        if (!Directory.Exists(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }
        
        _cachePath = Path.Combine(cacheDir, "manifest_cache.json");
    }

    public async Task<List<Mod>> FetchRemoteManifestAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(TargetUrl))
        {
            Log.Warning("[ManifestService] TargetUrl not configured");
            return new List<Mod>();
        }

        try
        {
            if (IsCacheFresh(out var cachedMods))
            {
                Log.Information("[ManifestService] Using cached manifest ({Count} mods)", cachedMods.Count);
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var refreshed = await FetchFromRemoteAsync(TargetUrl, CancellationToken.None);
                        if (refreshed?.Count > 0)
                        {
                            SaveCache(refreshed);
                            Log.Debug("[ManifestService] Background cache refresh completed ({Count} mods)", refreshed.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "[ManifestService] Background refresh failed (non-critical)");
                    }
                });

                return cachedMods;
            }

            Log.Information("[ManifestService] Fetching manifest from: {Url}", TargetUrl);
            var mods = await FetchFromRemoteAsync(TargetUrl, ct);

            if (mods?.Count > 0)
            {
                SaveCache(mods);
                Log.Information("[ManifestService] Successfully fetched {Count} mods", mods.Count);
                return mods;
            }

            if (TryLoadCache(out var fallbackMods))
            {
                Log.Warning("[ManifestService] Remote manifest empty, using cached fallback ({Count} mods)", fallbackMods.Count);
                return fallbackMods;
            }

            return new List<Mod>();
        }
        catch (OperationCanceledException)
        {
            Log.Information("[ManifestService] Manifest fetch cancelled");
            
            if (TryLoadCache(out var cached))
            {
                return cached;
            }
            
            return new List<Mod>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ManifestService] Failed to fetch manifest");
            
            if (TryLoadCache(out var cached))
            {
                Log.Warning("[ManifestService] Returning stale cache after error ({Count} mods)", cached.Count);
                return cached;
            }
            
            return new List<Mod>();
        }
    }

    private async Task<List<Mod>> FetchFromRemoteAsync(string url, CancellationToken ct)
    {
        try
        {
            var cacheBuster = url.Contains('?') ? $"&_t={DateTime.UtcNow.Ticks}" : $"?_t={DateTime.UtcNow.Ticks}";
            var requestUrl = $"{url}{cacheBuster}";

            Log.Debug("[ManifestService] Requesting: {Url}", requestUrl);

            var json = await _httpClient.GetStringAsync(requestUrl, ct);

            if (string.IsNullOrWhiteSpace(json))
            {
                Log.Warning("[ManifestService] Received empty response");
                return new List<Mod>();
            }

            var mods = JsonConvert.DeserializeObject<List<Mod>>(json, JsonSettings);

            if (mods == null || mods.Count == 0)
            {
                Log.Warning("[ManifestService] Parsed manifest contains no mods");
                return new List<Mod>();
            }

            foreach (var mod in mods)
            {
                try
                {
                    mod.FinalizeFromManifest();
                    mod.Source = "Remote";
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[ManifestService] Failed to finalize mod: {ModId}", mod.Id);
                }
            }

            Log.Information("[ManifestService] Successfully parsed {Count} mods", mods.Count);
            return mods;
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "[ManifestService] Network error fetching manifest");
            throw;
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "[ManifestService] JSON parsing error");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ManifestService] Unexpected error fetching manifest");
            throw;
        }
    }

    private bool IsCacheFresh(out List<Mod> mods)
    {
        mods = new List<Mod>();

        try
        {
            if (!File.Exists(_cachePath))
            {
                Log.Debug("[ManifestService] No cache file found");
                return false;
            }

            var info = new FileInfo(_cachePath);
            var age = DateTime.UtcNow - info.LastWriteTimeUtc;

            if (age > _cacheTtl)
            {
                Log.Debug("[ManifestService] Cache is stale ({Age} old, TTL: {TTL})", age, _cacheTtl);
                return false;
            }

            if (TryLoadCache(out mods))
            {
                Log.Debug("[ManifestService] Cache is fresh ({Age} old)", age);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ManifestService] Cache freshness check failed");
            return false;
        }
    }

    private bool TryLoadCache(out List<Mod> mods)
    {
        mods = new List<Mod>();

        try
        {
            if (!File.Exists(_cachePath))
            {
                return false;
            }

            var json = File.ReadAllText(_cachePath);
            
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            mods = JsonConvert.DeserializeObject<List<Mod>>(json, JsonSettings) ?? new List<Mod>();

            foreach (var mod in mods)
            {
                try
                {
                    mod.FinalizeFromManifest();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[ManifestService] Failed to finalize cached mod: {ModId}", mod.Id);
                }
            }

            Log.Debug("[ManifestService] Loaded {Count} mods from cache", mods.Count);
            return mods.Count > 0;
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "[ManifestService] Failed to parse cache (corrupted?)");
            
            try
            {
                File.Delete(_cachePath);
            }
            catch { }
            
            return false;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ManifestService] Failed to load cache");
            return false;
        }
    }

    private void SaveCache(List<Mod> mods)
    {
        try
        {
            var json = JsonConvert.SerializeObject(mods, Formatting.Indented, JsonSettings);
            File.WriteAllText(_cachePath, json);
            
            Log.Debug("[ManifestService] Saved {Count} mods to cache: {Path}", mods.Count, _cachePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ManifestService] Failed to save cache (non-critical)");
        }
    }

    public void ClearCache()
    {
        try
        {
            if (File.Exists(_cachePath))
            {
                File.Delete(_cachePath);
                Log.Information("[ManifestService] Cache cleared");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ManifestService] Failed to clear cache");
        }
    }

    public (bool Exists, DateTime? LastModified, long Size) GetCacheInfo()
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return (false, null, 0);
            }

            var info = new FileInfo(_cachePath);
            return (true, info.LastWriteTimeUtc, info.Length);
        }
        catch
        {
            return (false, null, 0);
        }
    }
}