using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Yellowcake.Services;

public class ThemeService
{
    private static readonly string ExternalThemesFolder = Path.Combine(AppContext.BaseDirectory, "Themes");
    private const string ThemeConfigKey = "SelectedTheme";
    private const string DefaultTheme = "Dark";

    public static DatabaseService? Database { get; set; }

    public ThemeService()
    {
        if (!Directory.Exists(ExternalThemesFolder))
            Directory.CreateDirectory(ExternalThemesFolder);
    }

    public List<string> GetAvailableThemes()
    {
        var themes = new List<string> { "Dark", "Light", "Synthium" };

        try
        {
            if (Directory.Exists(ExternalThemesFolder))
            {
                var customThemes = Directory.EnumerateFiles(ExternalThemesFolder, "*.axaml")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => name != null && !themes.Contains(name, StringComparer.OrdinalIgnoreCase));

                themes.AddRange(customThemes!);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to scan themes");
        }

        return themes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void ApplyTheme(string? themeName = null)
    {
        var app = Application.Current;
        if (app?.Resources == null) return;

        themeName ??= Database?.GetSetting(ThemeConfigKey) ?? DefaultTheme;

        try
        {
            var dictionary = LoadThemeResource(themeName);

            if (dictionary == null && themeName != DefaultTheme)
            {
                dictionary = LoadThemeResource(DefaultTheme);
                themeName = DefaultTheme;
            }

            if (dictionary != null)
            {
                var merged = app.Resources.MergedDictionaries;

                if (merged.Count > 1)
                    merged[1] = dictionary;
                else
                    merged.Add(dictionary);

                app.RequestedThemeVariant = GetVariantFromName(themeName);
                Database?.SaveSetting(ThemeConfigKey, themeName);

                Log.Information("Applied Theme: {Theme}", themeName);
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Theme failure");
            app.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    private ResourceDictionary? LoadThemeResource(string themeName)
    {
        string externalPath = Path.Combine(ExternalThemesFolder, $"{themeName}.axaml");

        if (File.Exists(externalPath))
        {
            try
            {
                return (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(externalPath, UriKind.Absolute));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "External XAML error: {Path}", externalPath);
            }
        }

        try
        {
            var uri = new Uri($"avares://Yellowcake/Themes/{themeName}.axaml");
            return (ResourceDictionary)AvaloniaXamlLoader.Load(uri);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Internal theme error: {Theme}", themeName);
            return null;
        }
    }

    private ThemeVariant GetVariantFromName(string name)
    {
        return name.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    public void Initialize() => ApplyTheme();
}