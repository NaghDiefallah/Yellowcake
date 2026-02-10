using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Yellowcake.Services;

namespace Yellowcake.Models;

public partial class Mod : ObservableObject
{
    private static readonly HttpClient _httpClient = new(new HttpClientHandler 
    { 
        AllowAutoRedirect = true,
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly Regex VersionRegex = new(@"^(\d+\.)+\d+", RegexOptions.Compiled);

    #region Core Properties

    [BsonId]
    [ObservableProperty]
    [JsonProperty("id")]
    private string _id = string.Empty;

    [ObservableProperty]
    [JsonProperty("displayName")]
    private string _name = string.Empty;

    [ObservableProperty]
    [JsonProperty("description")]
    private string? _description;

    [ObservableProperty]
    [JsonProperty("infoUrl")]
    private string? _infoUrl;

    [ObservableProperty]
    [JsonProperty("authors")]
    private List<string> _authors = new();

    [ObservableProperty]
    [JsonProperty("tags")]
    private List<string> _tags = new();

    [ObservableProperty]
    [JsonProperty("artifacts")]
    private List<Artifact> _artifacts = new();

    [ObservableProperty]
    [JsonProperty("screenshots")]
    private List<string> _screenshotUrls = new();

    [ObservableProperty]
    [JsonProperty("previewImage")]
    private string? _previewImageUrl;

    #endregion

    #region Runtime Properties

    [ObservableProperty]
    [JsonIgnore]
    private string _source = "Remote";

    [ObservableProperty]
    [JsonIgnore]
    private bool _isInstalled;

    [ObservableProperty]
    [JsonIgnore]
    private bool _isEnabled;

    [ObservableProperty]
    [JsonIgnore]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    private bool _hasUpdate;

    [ObservableProperty]
    [JsonIgnore]
    private string? _installedVersionString;

    [ObservableProperty]
    [JsonIgnore]
    private string? _installedArtifactHash;

    [ObservableProperty]
    [JsonIgnore]
    private string? _changelog;

    [BsonIgnore]
    [ObservableProperty]
    [JsonIgnore]
    private bool _hasScreenshots;

    #endregion

    #region UI State

    [BsonIgnore]
    [ObservableProperty]
    [JsonIgnore]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    private bool _isDownloading;

    [BsonIgnore]
    [ObservableProperty]
    [JsonIgnore]
    [NotifyPropertyChangedFor(nameof(DownloadProgressText))]
    private double _downloadProgress;

    [BsonIgnore]
    [ObservableProperty]
    [JsonIgnore]
    private bool _isSelectedInBatch;

    #endregion

    #region Categorization

    [ObservableProperty]
    [JsonIgnore]
    private bool _isVoicePack;

    [ObservableProperty]
    [JsonIgnore]
    private bool _isLivery;

    [ObservableProperty]
    [JsonIgnore]
    private bool _isMission;

    #endregion

    #region Legacy

    [BsonIgnore]
    [ObservableProperty]
    [JsonIgnore]
    private ObservableCollection<Mod> _addons = new();

    [ObservableProperty]
    [JsonIgnore]
    private string? _latestVersion;

    [BsonIgnore, JsonIgnore]
    public string? ExpectedHash { get; set; }

    #endregion

    #region Computed Properties

    private Artifact? _cachedLatestArtifact;
    private bool _artifactCached;

    [BsonIgnore, JsonIgnore]
    public Artifact? LatestArtifact
    {
        get
        {
            if (_artifactCached) return _cachedLatestArtifact;

            _cachedLatestArtifact = Artifacts?
                .Where(a => string.Equals(a.Category, "release", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => ParseVersionSafe(a.Version))
                .FirstOrDefault();

            _artifactCached = true;
            return _cachedLatestArtifact;
        }
    }

    [BsonIgnore, JsonIgnore]
    public string Version => LatestArtifact?.Version ?? InstalledVersionString ?? "0.0.0";

    [BsonIgnore, JsonIgnore]
    public string? DownloadUrl => LatestArtifact?.DownloadUrl;

    [BsonIgnore, JsonIgnore]
    public string? Hash => LatestArtifact?.Hash;

    [BsonIgnore, JsonIgnore]
    public string Category => LatestArtifact?.Type ?? "Plugin";

    [BsonIgnore, JsonIgnore]
    public List<string> Dependencies => LatestArtifact?.Dependencies ?? new List<string>();

    [BsonIgnore, JsonIgnore]
    public List<string> Conflicts => LatestArtifact?.Conflicts ?? new List<string>();

    [BsonIgnore, JsonIgnore]
    public long FileSizeBytes => LatestArtifact?.FileSizeBytes ?? 0;

    [BsonIgnore, JsonIgnore]
    public string? ThumbnailUrl
    {
        get
        {
            var url = LatestArtifact?.ThumbnailUrl;
            return string.IsNullOrWhiteSpace(url) || url == "null" ? null : url;
        }
    }

    [BsonIgnore, JsonIgnore]
    public string? GameVersion => LatestArtifact?.GameVersion;

    [BsonIgnore, JsonIgnore]
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailUrl);

    [BsonIgnore, JsonIgnore]
    public string FileSizeFormatted => FileSizeBytes > 0 ? FormatFileSize(FileSizeBytes) : "Unknown";

    [BsonIgnore, JsonIgnore]
    public bool HasDependencies => Dependencies?.Any() == true;

    [BsonIgnore, JsonIgnore]
    public bool HasConflicts => Conflicts?.Any() == true;

    [BsonIgnore, JsonIgnore]
    public string Author => Authors?.FirstOrDefault() ?? "Unknown";

    [BsonIgnore, JsonIgnore]
    public bool IsAddon => !string.IsNullOrWhiteSpace(Source) && Source != "Remote";

    [BsonIgnore, JsonIgnore]
    public bool CanUpdate => HasUpdate && !IsDownloading;

    [BsonIgnore, JsonIgnore]
    public string? EffectiveHash => ExpectedHash ?? Hash;

    [BsonIgnore, JsonIgnore]
    public string? EffectiveUrl => DownloadUrl;

    [BsonIgnore, JsonIgnore]
    public bool ShouldVerifyHash
    {
        get
        {
            var hash = EffectiveHash;
            return !string.IsNullOrWhiteSpace(hash) &&
                   !string.Equals(hash, "null", StringComparison.OrdinalIgnoreCase) &&
                   hash.Contains("sha256:", StringComparison.OrdinalIgnoreCase);
        }
    }

    [BsonIgnore, JsonIgnore]
    public string DownloadProgressText => $"{DownloadProgress:F1}%";

    [BsonIgnore, JsonIgnore]
    public int ArtifactCount => Artifacts?.Count ?? 0;

    [BsonIgnore, JsonIgnore]
    public bool HasMultipleVersions => ArtifactCount > 1;

    #endregion

    #region Methods

    partial void OnScreenshotUrlsChanged(List<string> value)
    {
        HasScreenshots = value?.Any() ?? false;
    }

    public void FinalizeFromManifest()
    {
        var nameUpper = Name?.ToUpperInvariant() ?? "";
        var descUpper = Description?.ToUpperInvariant() ?? "";
        
        var hasVoiceTag = Tags?.Any(t => t.Contains("voice", StringComparison.OrdinalIgnoreCase)) ?? false;
        var hasVoiceType = Category.Contains("voice", StringComparison.OrdinalIgnoreCase) || 
                           Category.Contains("audio", StringComparison.OrdinalIgnoreCase);
        
        IsVoicePack = hasVoiceTag || hasVoiceType;
        
        IsLivery = Tags?.Any(t => t.Contains("livery", StringComparison.OrdinalIgnoreCase)) ?? false ||
                   Category.Equals("Livery", StringComparison.OrdinalIgnoreCase) ||
                   nameUpper.Contains("LIVERY") || nameUpper.Contains("SKIN") || nameUpper.Contains("PAINT");
        
        IsMission = Tags?.Any(t => t.Contains("mission", StringComparison.OrdinalIgnoreCase)) ?? false ||
                    Category.Equals("Mission", StringComparison.OrdinalIgnoreCase) ||
                    nameUpper.Contains("MISSION") || nameUpper.Contains("CAMPAIGN");
    }

    public async Task<long?> FetchFileSizeAsync(CancellationToken ct = default)
    {
        var url = EffectiveUrl;
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            if (ProtonDriveService.IsProtonDriveUrl(url))
            {
                var protonService = new ProtonDriveService();
                var size = await protonService.GetFileSizeAsync(url, ct);
                if (size.HasValue && LatestArtifact != null)
                {
                    LatestArtifact.FileSizeBytes = size.Value;
                    OnPropertyChanged(nameof(FileSizeBytes));
                    OnPropertyChanged(nameof(FileSizeFormatted));
                }
                return size;
            }

            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength.HasValue)
            {
                var size = response.Content.Headers.ContentLength.Value;
                if (LatestArtifact != null)
                {
                    LatestArtifact.FileSizeBytes = size;
                    OnPropertyChanged(nameof(FileSizeBytes));
                    OnPropertyChanged(nameof(FileSizeFormatted));
                }
                return size;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to fetch size for {Mod}", Name);
        }

        return null;
    }

    public Artifact? GetArtifactByVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || Artifacts == null) return null;
        return Artifacts.FirstOrDefault(a => string.Equals(a.Version, version, StringComparison.OrdinalIgnoreCase));
    }

    public List<Artifact> GetReleaseArtifacts()
    {
        if (Artifacts == null || Artifacts.Count == 0) return new List<Artifact>();
        return Artifacts
            .Where(a => string.Equals(a.Category, "release", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => ParseVersionSafe(a.Version))
            .ToList();
    }

    public List<Artifact> GetBetaArtifacts()
    {
        if (Artifacts == null) return new List<Artifact>();
        return Artifacts
            .Where(a => a.IsBeta || a.IsAlpha)
            .OrderByDescending(a => ParseVersionSafe(a.Version))
            .ToList();
    }

    public void MarkAsInstalled(string version, string? hash = null)
    {
        IsInstalled = true;
        InstalledVersionString = version;
        InstalledArtifactHash = hash;
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(Version));
    }

    public void MarkAsUninstalled()
    {
        IsInstalled = false;
        IsEnabled = false;
        HasUpdate = false;
        InstalledVersionString = null;
        InstalledArtifactHash = null;
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(HasUpdate));
    }

    public (bool IsValid, string? Error) Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) return (false, "Mod ID is missing");
        if (string.IsNullOrWhiteSpace(Name)) return (false, "Mod name is missing");
        if (Artifacts == null || Artifacts.Count == 0) return (false, "No artifacts available");
        
        var latest = LatestArtifact;
        if (latest == null) return (false, "No release artifact found");
        if (string.IsNullOrWhiteSpace(latest.DownloadUrl)) return (false, "Download URL is missing");

        return (true, null);
    }

    public int CompareVersion(Mod other)
    {
        if (other == null) return 1;
        try
        {
            var thisVer = ParseVersionSafe(this.Version);
            var otherVer = ParseVersionSafe(other.Version);
            return thisVer.CompareTo(otherVer);
        }
        catch
        {
            return string.Compare(this.Version, other.Version, StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsNewerThan(string version)
    {
        try
        {
            var thisVer = ParseVersionSafe(this.Version);
            var compareVer = ParseVersionSafe(version);
            return thisVer > compareVer;
        }
        catch
        {
            return false;
        }
    }

    public override string ToString() => $"{Name} v{Version} ({Id})";

    #endregion

    #region Static Methods

    public static string CleanVersion(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "0.0.0";
        v = v.TrimStart('v', 'V');
        var match = VersionRegex.Match(v);
        return match.Success ? match.Value : "0.0.0";
    }

    private static System.Version ParseVersionSafe(string? versionString)
    {
        try
        {
            var cleaned = CleanVersion(versionString);
            return System.Version.TryParse(cleaned, out var version) 
                ? version 
                : new System.Version(0, 0, 0);
        }
        catch
        {
            return new System.Version(0, 0, 0);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "Unknown";
        
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return suffixIndex == 0 
            ? $"{size:F0} {suffixes[suffixIndex]}" 
            : $"{size:F2} {suffixes[suffixIndex]}";
    }

    #endregion
}

public class Artifact
{
    [JsonProperty("category")]
    public string Category { get; set; } = "release";

    [JsonProperty("conflicts")]
    public List<string> Conflicts { get; set; } = new();

    [JsonProperty("depenencies")]
    public List<string> Dependencies { get; set; } = new();

    [JsonProperty("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonProperty("fileName")]
    public string? FileName { get; set; }

    [JsonProperty("fileSizeBytes")]
    public long FileSizeBytes { get; set; }

    [JsonProperty("gameVersion")]
    public string? GameVersion { get; set; }

    [JsonProperty("hash")]
    public string? Hash { get; set; }

    [JsonProperty("thumbnailUrl")]
    public string? ThumbnailUrl { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; } = "Plugin";

    [JsonProperty("version")]
    public string Version { get; set; } = "0.0.0";

    [JsonIgnore]
    public bool IsRelease => string.Equals(Category, "release", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsBeta => Category?.Contains("beta", StringComparison.OrdinalIgnoreCase) == true;

    [JsonIgnore]
    public bool IsAlpha => Category?.Contains("alpha", StringComparison.OrdinalIgnoreCase) == true;

    [JsonIgnore]
    public string? CleanHash => Hash?.Replace("sha256:", "", StringComparison.OrdinalIgnoreCase)?.Trim();

    [JsonIgnore]
    public bool HasDependencies => Dependencies?.Any() == true;

    [JsonIgnore]
    public bool HasConflicts => Conflicts?.Any() == true;

    [JsonIgnore]
    public string DisplayName => $"v{Version} ({Category})";

    public override string ToString() => $"{Version} - {Type} ({Category})";
}

public enum ModFilter
{
    All,
    Plugins,
    Voice,
    Livery,
    Missions,
    Installed
}