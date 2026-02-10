using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Yellowcake.Models;

namespace Yellowcake.Services;

public class DependencyResolver
{
    private readonly DatabaseService _db;
    private readonly List<Mod> _availableMods;

    public DependencyResolver(DatabaseService db, List<Mod> availableMods)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _availableMods = availableMods ?? throw new ArgumentNullException(nameof(availableMods));
    }

    public async Task<DependencyResult> ResolveDependenciesAsync(Mod mod)
    {
        var result = new DependencyResult { TargetMod = mod };
        var visited = new HashSet<string>();
        var queue = new Queue<Mod>();
        queue.Enqueue(mod);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current.Id))
                continue;

            var dependencies = current.Dependencies;
            if (dependencies == null || dependencies.Count == 0)
                continue;

            foreach (var depId in dependencies)
            {
                var installed = _db.GetAll<Mod>("installed_mods")
                    .FirstOrDefault(m => m.Id.Equals(depId, StringComparison.OrdinalIgnoreCase));

                if (installed != null)
                {
                    result.Satisfied.Add(installed);
                    continue;
                }

                var available = _availableMods.FirstOrDefault(m => 
                    m.Id.Equals(depId, StringComparison.OrdinalIgnoreCase));
                
                if (available != null)
                {
                    result.Missing.Add(available);
                    queue.Enqueue(available);
                }
                else
                {
                    result.Unresolved.Add(depId);
                    Log.Warning("Could not resolve dependency: {DependencyId} for mod {ModId}", depId, current.Id);
                }
            }
        }

        result.HasCircularDependency = DetectCircularDependencies(mod, new HashSet<string>());

        return result;
    }

    private bool DetectCircularDependencies(Mod mod, HashSet<string> visited)
    {
        if (!visited.Add(mod.Id))
            return true; 

        var dependencies = mod.Dependencies;
        if (dependencies == null || dependencies.Count == 0)
            return false;

        foreach (var depId in dependencies)
        {
            var depMod = _db.GetAll<Mod>("installed_mods")
                .FirstOrDefault(m => m.Id.Equals(depId, StringComparison.OrdinalIgnoreCase));

            if (depMod != null && DetectCircularDependencies(depMod, new HashSet<string>(visited)))
                return true;
        }

        return false;
    }

    public List<ModConflict> DetectConflicts(List<Mod> installedMods)
    {
        var conflicts = new List<ModConflict>();

        for (int i = 0; i < installedMods.Count; i++)
        {
            for (int j = i + 1; j < installedMods.Count; j++)
            {
                var mod1 = installedMods[i];
                var mod2 = installedMods[j];

                var mod1Conflicts = mod1.Conflicts;
                var mod2Conflicts = mod2.Conflicts;

                if (mod1Conflicts?.Contains(mod2.Id, StringComparer.OrdinalIgnoreCase) == true || 
                    mod2Conflicts?.Contains(mod1.Id, StringComparer.OrdinalIgnoreCase) == true)
                {
                    conflicts.Add(new ModConflict
                    {
                        Mod1 = mod1,
                        Mod2 = mod2,
                        ConflictType = ConflictType.Incompatible,
                        Severity = ConflictSeverity.Critical,
                        Description = $"{mod1.Name} is incompatible with {mod2.Name}"
                    });
                }

                if (!string.IsNullOrEmpty(mod1.Category) && 
                    !string.IsNullOrEmpty(mod2.Category) &&
                    mod1.Category.Equals(mod2.Category, StringComparison.OrdinalIgnoreCase) &&
                    (mod1.Category.Equals("Voice Pack", StringComparison.OrdinalIgnoreCase) ||
                     mod1.Category.Equals("Livery", StringComparison.OrdinalIgnoreCase)))
                {
                    conflicts.Add(new ModConflict
                    {
                        Mod1 = mod1,
                        Mod2 = mod2,
                        ConflictType = ConflictType.DuplicateFunctionality,
                        Severity = ConflictSeverity.Low,
                        Description = $"{mod1.Name} and {mod2.Name} may conflict as they are both {mod1.Category}s"
                    });
                }

                if (mod1.Dependencies?.Contains(mod2.Id, StringComparer.OrdinalIgnoreCase) == true &&
                    mod2.Dependencies?.Contains(mod1.Id, StringComparer.OrdinalIgnoreCase) == true)
                {
                    conflicts.Add(new ModConflict
                    {
                        Mod1 = mod1,
                        Mod2 = mod2,
                        ConflictType = ConflictType.CircularDependency,
                        Severity = ConflictSeverity.High,
                        Description = $"{mod1.Name} and {mod2.Name} have circular dependencies"
                    });
                }
            }
        }

        return conflicts;
    }

    public List<string> GetMissingDependencies(Mod mod, List<Mod> installedMods)
    {
        var missing = new List<string>();
        var dependencies = mod.Dependencies;
        
        if (dependencies == null || dependencies.Count == 0)
            return missing;

        var installedIds = installedMods.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var depId in dependencies)
        {
            if (!installedIds.Contains(depId))
            {
                missing.Add(depId);
            }
        }

        return missing;
    }

    public bool CanInstall(Mod mod, List<Mod> installedMods)
    {
        var missing = GetMissingDependencies(mod, installedMods);
        return missing.Count == 0;
    }
}

public class DependencyResult
{
    public Mod TargetMod { get; set; } = null!;
    public List<Mod> Satisfied { get; set; } = new();
    public List<Mod> Missing { get; set; } = new();
    public List<string> Unresolved { get; set; } = new();
    public bool HasCircularDependency { get; set; }
    public bool IsResolved => Missing.Count == 0 && Unresolved.Count == 0 && !HasCircularDependency;
    
    public string GetSummary()
    {
        if (IsResolved)
            return "All dependencies satisfied";
        
        var parts = new List<string>();
        if (Missing.Count > 0)
            parts.Add($"{Missing.Count} missing");
        if (Unresolved.Count > 0)
            parts.Add($"{Unresolved.Count} unresolved");
        if (HasCircularDependency)
            parts.Add("circular dependency detected");
        
        return string.Join(", ", parts);
    }
}

public class ModConflict
{
    public Mod Mod1 { get; set; } = null!;
    public Mod Mod2 { get; set; } = null!;
    public ConflictType ConflictType { get; set; }
    public ConflictSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    
    public string GetRecommendation()
    {
        return Severity switch
        {
            ConflictSeverity.Critical => "These mods should not be used together. Uninstall one of them.",
            ConflictSeverity.High => "These mods may cause issues. Consider using only one.",
            ConflictSeverity.Medium => "These mods might conflict. Test carefully.",
            ConflictSeverity.Low => "Minor conflict. Usually safe to use together.",
            _ => "Unknown conflict level"
        };
    }
}

public enum ConflictType
{
    Incompatible,
    DuplicateFunctionality,
    CircularDependency
}

public enum ConflictSeverity
{
    Low,
    Medium,
    High,
    Critical
}