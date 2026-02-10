using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Yellowcake.Models;
using Yellowcake.Services;

namespace Yellowcake.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task ShowModDetails(Mod? mod)
    {
        if (mod == null)
        {
            Log.Warning("Attempted to show details for null mod");
            return;
        }

        try
        {
            DetailsMod = mod;
            await LoadModExtendedInfo(mod);
            IsDetailsOpen = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to show mod details for {Mod}", mod.Name);
            IsDetailsOpen = false;
            DetailsMod = null;
        }
    }

    [RelayCommand]
    private void CloseModDetails()
    {
        IsDetailsOpen = false;
        
        _ = Task.Delay(200).ContinueWith(_ =>
        {
            DetailsMod = null;
            ModChangelog = string.Empty;
            ModDependencies = new List<Mod>();
            ModConflicts = new List<Mod>();
            DependencyGraph = string.Empty;
        });
    }

    private async Task LoadModExtendedInfo(Mod mod)
    {
        try
        {
            ModChangelog = GenerateChangelog(mod);
            ModDependencies = ResolveDependencies(mod);
            ModConflicts = ResolveConflicts(mod);
            DependencyGraph = GenerateDependencyGraph(mod);
            
            if (mod.FileSizeBytes <= 0)
            {
                await mod.FetchFileSizeAsync(_shutdownCts.Token);
                OnPropertyChanged(nameof(DetailsMod));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load extended info for {Mod}", mod.Name);
        }
    }

    private string GenerateChangelog(Mod mod)
    {
        if (mod == null) return string.Empty;

        if (!string.IsNullOrWhiteSpace(mod.Changelog))
            return mod.Changelog;

        var currentVersion = mod.Version;
        var latestVersion = mod.LatestVersion ?? currentVersion;

        if (currentVersion == latestVersion)
            return "No updates available";

        var artifacts = mod.GetReleaseArtifacts();
        var changelogText = new System.Text.StringBuilder();

        changelogText.AppendLine($"Version {currentVersion} → {latestVersion}");
        changelogText.AppendLine();

        if (artifacts.Count > 1)
        {
            changelogText.AppendLine("📦 Available Versions:");
            foreach (var artifact in artifacts.Take(5))
            {
                var gameVer = artifact.GameVersion ?? "Unknown";
                changelogText.AppendLine($"  • v{artifact.Version} (Game: {gameVer})");
            }
            changelogText.AppendLine();
        }

        changelogText.AppendLine("✨ Features");
        changelogText.AppendLine("- Check the mod's project page for full changelog");
        changelogText.AppendLine();
        changelogText.AppendLine("📦 Package Information");
        changelogText.AppendLine($"- Category: {mod.Category}");
        changelogText.AppendLine($"- Authors: {string.Join(", ", mod.Authors ?? new List<string> { "Unknown" })}");
        changelogText.AppendLine($"- Game Version: {mod.GameVersion ?? "Unknown"}");
        changelogText.AppendLine($"- File Size: {mod.FileSizeFormatted}");

        return changelogText.ToString();
    }

    private List<Mod> ResolveDependencies(Mod mod)
    {
        var dependencies = mod.Dependencies;
        
        if (dependencies == null || !dependencies.Any())
            return new List<Mod>();

        var resolved = new List<Mod>();

        foreach (var depId in dependencies)
        {
            var depMod = _allRemoteMods.FirstOrDefault(m => 
                string.Equals(m.Id, depId, StringComparison.OrdinalIgnoreCase));

            depMod ??= _installedMods.FirstOrDefault(m => 
                string.Equals(m.Id, depId, StringComparison.OrdinalIgnoreCase));

            if (depMod != null)
            {
                resolved.Add(depMod);
            }
            else
            {
                var placeholder = new Mod
                {
                    Id = depId,
                    Name = $"⚠ {depId}",
                    Description = "This dependency could not be found in the mod repository",
                    IsInstalled = false,
                    Source = "Missing"
                };

                placeholder.Artifacts.Add(new Artifact
                {
                    Type = "Unknown",
                    Category = "release",
                    Version = "0.0.0"
                });

                resolved.Add(placeholder);
                Log.Warning("Could not resolve dependency: {DependencyId} for mod {ModName}", depId, mod.Name);
            }
        }

        return resolved;
    }

    private List<Mod> ResolveConflicts(Mod mod)
    {
        if (mod == null) return new List<Mod>();

        var conflicts = new List<Mod>();
        var modConflicts = mod.Conflicts;

        if (modConflicts?.Any() != true)
        {
            return conflicts;
        }

        foreach (var conflictId in modConflicts)
        {
            var conflictMod = _installedMods.FirstOrDefault(m => 
                string.Equals(m.Id, conflictId, StringComparison.OrdinalIgnoreCase));

            if (conflictMod != null)
            {
                conflicts.Add(conflictMod);
            }
            else
            {
                var remoteMod = _allRemoteMods.FirstOrDefault(m => 
                    string.Equals(m.Id, conflictId, StringComparison.OrdinalIgnoreCase));

                if (remoteMod != null)
                {
                    conflicts.Add(remoteMod);
                }
            }
        }

        return conflicts;
    }

    private string GenerateDependencyGraph(Mod mod)
    {
        var dependencies = mod.Dependencies;
        
        if (dependencies == null || !dependencies.Any())
            return string.Empty;

        var graph = new System.Text.StringBuilder();
        graph.AppendLine("graph TD");
        
        var rootId = SanitizeGraphId(mod.Name);
        graph.AppendLine($"    {rootId}[\"{EscapeGraphText(mod.Name)}\"]");
        graph.AppendLine($"    style {rootId} fill:#6366f1,stroke:#4f46e5,stroke-width:2px,color:#fff");

        foreach (var depId in dependencies)
        {
            var dep = _allRemoteMods.FirstOrDefault(m => m.Id == depId) ?? 
                     _installedMods.FirstOrDefault(m => m.Id == depId);

            if (dep != null)
            {
                var depNodeId = SanitizeGraphId(dep.Name);
                var statusColor = dep.IsInstalled ? "#10b981" : "#f59e0b";
                
                graph.AppendLine($"    {depNodeId}[\"{EscapeGraphText(dep.Name)}\"]");
                graph.AppendLine($"    {rootId} --> {depNodeId}");
                graph.AppendLine($"    style {depNodeId} fill:{statusColor},stroke:#333,color:#fff");

                var depDependencies = dep.Dependencies;
                if (depDependencies?.Any() == true)
                {
                    foreach (var nestedDepId in depDependencies.Take(3))
                    {
                        var nestedDep = _allRemoteMods.FirstOrDefault(m => m.Id == nestedDepId);
                        if (nestedDep != null)
                        {
                            var nestedNodeId = SanitizeGraphId(nestedDep.Name);
                            graph.AppendLine($"    {nestedNodeId}[\"{EscapeGraphText(nestedDep.Name)}\"]");
                            graph.AppendLine($"    {depNodeId} --> {nestedNodeId}");
                        }
                    }
                }
            }
            else
            {
                var missingId = $"Missing{SanitizeGraphId(depId)}";
                graph.AppendLine($"    {missingId}[\"❌ {EscapeGraphText(depId)}\"]");
                graph.AppendLine($"    {rootId} -.-> {missingId}");
                graph.AppendLine($"    style {missingId} fill:#ef4444,stroke:#dc2626,color:#fff");
            }
        }

        var modConflicts = mod.Conflicts;
        if (modConflicts?.Any() == true)
        {
            foreach (var conflictId in modConflicts.Take(3))
            {
                var conflict = _installedMods.FirstOrDefault(m => m.Id == conflictId);
                if (conflict != null)
                {
                    var conflictNodeId = SanitizeGraphId(conflict.Name);
                    graph.AppendLine($"    {conflictNodeId}[\"{EscapeGraphText(conflict.Name)}\"]");
                    graph.AppendLine($"    {rootId} -.->|⚠ conflicts| {conflictNodeId}");
                    graph.AppendLine($"    style {conflictNodeId} fill:#fbbf24,stroke:#f59e0b,color:#000");
                }
            }
        }

        return graph.ToString();
    }

    private static string SanitizeGraphId(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown";
        return new string(name.Where(c => char.IsLetterOrDigit(c)).ToArray());
    }

    private static string EscapeGraphText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Unknown";
        return text.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}