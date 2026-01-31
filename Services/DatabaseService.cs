using LiteDB;
using Yellowcake.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace Yellowcake.Services;

public class DatabaseService
{
    private readonly string _connectionString;
    public const string SettingsCollection = "settings";
    public const string AddonsCollection = "addons";

    public DatabaseService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dbDir = Path.Combine(appData, "Yellowcake");

        if (!Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        string dbPath = Path.Combine(dbDir, "data.db");
        _connectionString = $"Filename={dbPath};Connection=shared";

        BsonMapper.Global.Entity<Mod>().Id(m => m.Id);

        Log.Information("Database initialized for cross-platform use at: {Path}", dbPath);
    }

    private ILiteDatabase GetDatabase() => new LiteDatabase(_connectionString);

    public void Upsert<T>(string collectionName, T item) where T : class
    {
        try
        {
            using var db = GetDatabase();
            db.GetCollection<T>(collectionName).Upsert(item);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Data persistence error in {Collection}", collectionName);
        }
    }

    public List<T> GetAll<T>(string collectionName) where T : class
    {
        try
        {
            using var db = GetDatabase();
            return db.GetCollection<T>(collectionName).FindAll().ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Data retrieval error in {Collection}", collectionName);
            return [];
        }
    }

    public void Delete(string collectionName, BsonValue id)
    {
        try
        {
            using var db = GetDatabase();
            db.GetCollection(collectionName).Delete(id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Deletion error for ID {Id} in {Collection}", id, collectionName);
        }
    }

    public void SaveSetting(string key, object? value)
    {
        if (value == null) return;

        try
        {
            using var db = GetDatabase();
            db.GetCollection<SettingItem>(SettingsCollection).Upsert(new SettingItem
            {
                Id = key,
                Value = value.ToString()
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to persist setting: {Key}", key);
        }
    }

    public string? GetSetting(string key, string? defaultValue = null)
    {
        try
        {
            using var db = GetDatabase();
            var item = db.GetCollection<SettingItem>(SettingsCollection).FindById(key);
            return item?.Value ?? defaultValue;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Setting {Key} resolution failed; returning default.", key);
            return defaultValue;
        }
    }

    public T? GetSetting<T>(string key, T? defaultValue = default)
    {
        try
        {
            var value = GetSetting(key);
            if (value == null) return defaultValue;
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

    private class SettingItem
    {
        public string Id { get; set; } = string.Empty;
        public string? Value { get; set; }
    }
}