using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using LiteDB; 

namespace Yellowcake.Models;

public partial class Mod : ObservableObject
{
    [BsonId] 
    [ObservableProperty]
    [property: JsonPropertyName("Id")]
    private string _id = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("Name")]
    private string _name = string.Empty;

    [ObservableProperty]
    [property: JsonPropertyName("Description")]
    private string? _description;

    [ObservableProperty]
    [property: JsonPropertyName("GitHubUrl")]
    private string? _gitHubUrl;

    [ObservableProperty]
    [property: JsonPropertyName("Author")]
    private string _author = "Unknown";

    [ObservableProperty]
    [property: JsonPropertyName("Version")]
    private string _version = "1.0.0";

    [ObservableProperty]
    [property: JsonPropertyName("Category")]
    private string _category = "Plugin";

    [ObservableProperty]
    [property: JsonPropertyName("Dependencies")]
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

    [JsonIgnore]
    [BsonIgnore]
    public bool IsAddon => string.Equals(Category, "Addon", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Category, "Audio", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    [BsonIgnore]
    public bool CanUpdate => HasUpdate && !IsDownloading;

    public void FinalizeFromManifest()
    {
        if (string.IsNullOrWhiteSpace(Name)) Name = Id;

        LatestVersion ??= Version;

        IsVoicePack = string.Equals(Category, "Audio", StringComparison.OrdinalIgnoreCase);
        IsLivery = string.Equals(Category, "Visual", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Category, "Livery", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(Version) && !string.IsNullOrEmpty(LatestVersion))
        {
            HasUpdate = Version != LatestVersion;
        }
    }

    public override string ToString() => $"{Name} ({Version})";
}