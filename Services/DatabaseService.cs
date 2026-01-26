using LiteDB;
using Yellowcake.Models;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace Yellowcake.Services;

public class DatabaseService
{
    private readonly string _dbPath;

    public DatabaseService(string dbPath)
    {
        _dbPath = dbPath;
        // LiteDB creates the file and collections automatically on first use.
        Log.Information("DatabaseService initialized using LiteDB at {Path}", dbPath);
    }

    public void SaveSetting(string key, string? value)
    {
        using var db = new LiteDatabase(_dbPath);
        var settings = db.GetCollection<SettingItem>("settings");

        var item = new SettingItem { Id = key, Value = value };
        settings.Upsert(item);
    }

    public string? GetSetting(string key)
    {
        using var db = new LiteDatabase(_dbPath);
        return db.GetCollection<SettingItem>("settings").FindById(key)?.Value;
    }

    public void RegisterMod(Mod mod)
    {
        using var db = new LiteDatabase(_dbPath);
        var mods = db.GetCollection<Mod>("mods");

        // Upsert uses the 'Id' property of your Mod class as the primary key
        mods.Upsert(mod);
    }

    public void UpdateModEnabled(string modId, bool enabled)
    {
        using var db = new LiteDatabase(_dbPath);
        var mods = db.GetCollection<Mod>("mods");

        var mod = mods.FindById(modId);
        if (mod != null)
        {
            mod.IsEnabled = enabled;
            mods.Update(mod);
        }
    }

    public List<Mod> GetAllMods()
    {
        using var db = new LiteDatabase(_dbPath);
        return db.GetCollection<Mod>("mods").FindAll().ToList();
    }

    public void DeleteMod(string id)
    {
        using var db = new LiteDatabase(_dbPath);
        db.GetCollection<Mod>("mods").Delete(id);
    }

    // Small helper class for settings since LiteDB works best with typed objects
    private class SettingItem
    {
        public string Id { get; set; } = string.Empty; // LiteDB treats 'Id' as the primary key
        public string? Value { get; set; }
    }
}