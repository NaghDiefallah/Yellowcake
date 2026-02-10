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
    private readonly object _locker = new();

    public const string SettingsCollection = "settings";
    public const string AddonsCollection = "addons";

    public DatabaseService() : this(null) { }

    public DatabaseService(string? connectionStringOrPath)
    {
        if (string.IsNullOrWhiteSpace(connectionStringOrPath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dbDir = Path.Combine(appData, "Yellowcake");

            if (!Directory.Exists(dbDir))
                Directory.CreateDirectory(dbDir);

            string dbPath = Path.Combine(dbDir, "data.db");
            _connectionString = $"Filename={dbPath};Connection=shared";
            Log.Information("Database initialized for cross-platform use at: {Path}", dbPath);
        }
        else if (string.Equals(connectionStringOrPath, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            _connectionString = "Filename=:memory:;Connection=shared";
            Log.Information("Database initialized in-memory for tests.");
        }
        else if (connectionStringOrPath.Contains('='))
        {
            _connectionString = connectionStringOrPath;
            Log.Information("Database initialized with custom connection string");
        }
        else
        {
            var path = connectionStringOrPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _connectionString = $"Filename={path};Connection=shared";
            Log.Information("Database initialized at: {Path}", path);
        }

        BsonMapper.Global.Entity<Mod>().Id(m => m.Id);
    }

    private ILiteDatabase GetDatabase() => new LiteDatabase(_connectionString);

    public void Upsert<T>(string collectionName, T item) where T : class
    {
        if (item == null) return;

        try
        {
            lock (_locker)
            {
                using var db = GetDatabase();
                db.GetCollection<T>(collectionName).Upsert(item);
            }
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
            lock (_locker)
            {
                using var db = GetDatabase();
                return db.GetCollection<T>(collectionName).FindAll().ToList();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Data retrieval error in {Collection}", collectionName);
            return new List<T>();
        }
    }

    public void Delete(string collectionName, BsonValue id)
    {
        try
        {
            lock (_locker)
            {
                using var db = GetDatabase();
                db.GetCollection(collectionName).Delete(id);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Deletion error for ID {Id} in {Collection}", id, collectionName);
        }
    }

    public void Delete(string collectionName, string id)
        => Delete(collectionName, new BsonValue(id));

    public void SaveSetting(string key, object? value)
    {
        if (value == null) return;

        try
        {
            lock (_locker)
            {
                using var db = GetDatabase();
                db.GetCollection<SettingItem>(SettingsCollection).Upsert(new SettingItem
                {
                    Id = key,
                    Value = value.ToString()
                });
            }
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
            lock (_locker)
            {
                using var db = GetDatabase();
                var item = db.GetCollection<SettingItem>(SettingsCollection).FindById(key);
                return item?.Value ?? defaultValue;
            }
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

    public void DeleteAll(string collectionName)
    {
        try
        {
            var collection = GetDatabase().GetCollection(collectionName);
            collection.DeleteAll();
            Log.Debug("Deleted all records from collection {Collection}", collectionName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete all from collection {Collection}", collectionName);
            throw;
        }
    }

    private class SettingItem
    {
        public string Id { get; set; } = string.Empty;
        public string? Value { get; set; }
    }
}