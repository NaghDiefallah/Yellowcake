using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Yellowcake.Services;

public class LocalizationService
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    private Dictionary<string, string> _strings = new();
    private CultureInfo _currentCulture;

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            _currentCulture = value;
            LoadLanguage(value.TwoLetterISOLanguageName);
        }
    }

    private LocalizationService()
    {
        _currentCulture = CultureInfo.CurrentCulture;
        LoadLanguage(_currentCulture.TwoLetterISOLanguageName);
    }

    private void LoadLanguage(string languageCode)
    {
        try
        {
            var langPath = Path.Combine(AppContext.BaseDirectory, "Languages", $"{languageCode}.json");
            
            if (!File.Exists(langPath))
            {
                langPath = Path.Combine(AppContext.BaseDirectory, "Languages", "en.json");
            }

            if (File.Exists(langPath))
            {
                var json = File.ReadAllText(langPath);
                _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
        }
        catch
        {
            _strings = GetDefaultStrings();
        }
    }

    public string GetString(string key, params object[] args)
    {
        if (_strings.TryGetValue(key, out var value))
        {
            return args.Length > 0 ? string.Format(value, args) : value;
        }
        return key;
    }

    public string this[string key] => GetString(key);

    private Dictionary<string, string> GetDefaultStrings()
    {
        return new Dictionary<string, string>
        {
            // General
            { "app.title", "Yellowcake Mod Manager" },
            { "app.loading", "Loading..." },
            { "app.error", "Error" },
            { "app.success", "Success" },
            
            // Actions
            { "action.download", "Download" },
            { "action.install", "Install" },
            { "action.uninstall", "Uninstall" },
            { "action.enable", "Enable" },
            { "action.disable", "Disable" },
            { "action.refresh", "Refresh" },
            { "action.cancel", "Cancel" },
            { "action.save", "Save" },
            { "action.close", "Close" },
            
            // Mod Management
            { "mods.installed", "Installed Mods" },
            { "mods.available", "Available Mods" },
            { "mods.search", "Search mods..." },
            { "mods.filter", "Filter" },
            { "mods.sort", "Sort" },
            { "mods.dependencies", "Dependencies" },
            { "mods.conflicts", "Conflicts" },
            
            // Settings
            { "settings.title", "Settings" },
            { "settings.general", "General" },
            { "settings.appearance", "Appearance" },
            { "settings.advanced", "Advanced" },
            
            // Messages
            { "message.download_complete", "Download complete" },
            { "message.install_success", "Mod installed successfully" },
            { "message.uninstall_success", "Mod uninstalled successfully" },
            { "message.no_internet", "No internet connection" },
        };
    }

    public List<LanguageInfo> GetAvailableLanguages()
    {
        var languages = new List<LanguageInfo>
        {
            new() { Code = "en", Name = "English", NativeName = "English" },
            new() { Code = "es", Name = "Spanish", NativeName = "Español" },
            new() { Code = "fr", Name = "French", NativeName = "Français" },
            new() { Code = "de", Name = "German", NativeName = "Deutsch" },
            new() { Code = "it", Name = "Italian", NativeName = "Italiano" },
            new() { Code = "pt", Name = "Portuguese", NativeName = "Português" },
            new() { Code = "ru", Name = "Russian", NativeName = "Русский" },
            new() { Code = "ja", Name = "Japanese", NativeName = "日本語" },
            new() { Code = "zh", Name = "Chinese", NativeName = "中文" },
            new() { Code = "ko", Name = "Korean", NativeName = "한국어" },
        };

        return languages;
    }
}

public class LanguageInfo
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
}