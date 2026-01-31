using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Yellowcake.Models;

public partial class Mod : ObservableObject
{
    [BsonId]
    [ObservableProperty]
    [property: JsonPropertyName("id")]
    private string _id = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("displayName")]
    private string _name = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("description")]
    private string? _description;

    [ObservableProperty]
    [property: JsonPropertyName("authors")]
    private List<string> _authors = [];

    [ObservableProperty]
    [property: JsonPropertyName("tags")]
    private List<string> _tags = [];

    [ObservableProperty]
    [property: JsonPropertyName("infoUrl")]
    private string? _gitHubUrl;

    [ObservableProperty]
    [property: JsonIgnore]
    private string _version = "0.0.0";

    [ObservableProperty]
    [property: JsonIgnore]
    private string? _downloadUrl;

    [ObservableProperty]
    [property: JsonIgnore]
    private string? _expectedHash;

    [ObservableProperty]
    [property: JsonIgnore]
    private string _category = "plugin";

    [ObservableProperty]
    [property: JsonIgnore]
    private List<string> _dependencies = [];

    [BsonIgnore]
    [ObservableProperty]
    [property: JsonIgnore]
    private ObservableCollection<Mod> _addons = [];

    [ObservableProperty]
    [property: JsonIgnore]
    private string? _latestVersion;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isEnabled;

    [ObservableProperty]
    [property: JsonIgnore]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    private bool _hasUpdate;

    [BsonIgnore]
    [ObservableProperty]
    [property: JsonIgnore]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    private bool _isDownloading;

    [BsonIgnore]
    [ObservableProperty]
    [property: JsonIgnore]
    private double _downloadProgress;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isInstalled;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isVoicePack;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isLivery;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isMission;

    [ObservableProperty]
    [property: JsonIgnore]
    private string _source = "Primary";

    [JsonIgnore, BsonIgnore]
    public string Author => Authors?.FirstOrDefault() ?? "Unknown";

    [JsonIgnore, BsonIgnore]
    public bool IsAddon => string.Equals(Category, "addon", StringComparison.OrdinalIgnoreCase) || IsVoicePack || IsMission;

    [JsonIgnore, BsonIgnore]
    public bool CanUpdate => HasUpdate && !IsDownloading;

    [JsonIgnore, BsonIgnore]
    public bool ShouldVerifyHash => !IsVoicePack && !IsLivery && !IsMission && !string.Equals(Category, "addon", StringComparison.OrdinalIgnoreCase);

    public void FinalizeFromManifest()
    {
        if (string.IsNullOrWhiteSpace(Name)) Name = Id;

        LatestVersion ??= Version;

        var tagsSet = Tags?.Select(t => t.ToLowerInvariant()).ToHashSet() ?? [];
        var categoryLower = Category?.ToLowerInvariant() ?? string.Empty;
        var descLower = Description?.ToLowerInvariant() ?? string.Empty;
        var idLower = Id.ToLowerInvariant();

        IsVoicePack = tagsSet.Contains("voicepack") || categoryLower.Contains("voice") || descLower.Contains("[voice pack]");
        IsLivery = tagsSet.Contains("livery") || categoryLower.Contains("livery");
        IsMission = tagsSet.Contains("mission") || categoryLower.Contains("mission");

        if (!ShouldVerifyHash)
        {
            ExpectedHash = null;
        }

        UpdateUpdateStatus();
    }

    private void UpdateUpdateStatus()
    {
        if (string.IsNullOrWhiteSpace(Version) || string.IsNullOrWhiteSpace(LatestVersion)) return;

        var v1 = CleanVersion(Version);
        var v2 = CleanVersion(LatestVersion);

        if (System.Version.TryParse(v1, out var current) && System.Version.TryParse(v2, out var latest))
        {
            HasUpdate = latest > current;
        }
        else
        {
            HasUpdate = !string.Equals(v1, v2, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string CleanVersion(string v)
    {
        var match = Regex.Match(v, @"(\d+\.)*(\d+)");
        return match.Success ? match.Value : "0.0.0";
    }

    public override string ToString() => $"{Name} ({Version})";
}