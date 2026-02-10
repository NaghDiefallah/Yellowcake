using Serilog;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Yellowcake.Models;

namespace Yellowcake.Services;

public class InstallTransaction : IDisposable
{
    private readonly Mod _mod;
    private readonly InstallService _installService;
    private readonly DatabaseService _db;
    private string? _tempDirectory;
    private string? _backupDirectory;
    private bool _isCommitted;

    public InstallTransaction(Mod mod, InstallService installService, DatabaseService db)
    {
        _mod = mod ?? throw new ArgumentNullException(nameof(mod));
        _installService = installService ?? throw new ArgumentNullException(nameof(installService));
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task BeginAsync()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"yellowcake-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        
        Log.Information("Transaction started for {ModName}", _mod.Name);
    }

    public async Task ExtractAsync()
    {
        if (_tempDirectory == null)
            throw new InvalidOperationException("Transaction not started");

        Log.Information("Extracting {ModName} to temporary location", _mod.Name);
    }

    public async Task VerifyAsync()
    {
        if (_tempDirectory == null)
            throw new InvalidOperationException("Transaction not started");

        var isValid = await Task.Run(() => _installService.VerifyInstallation(_mod));
        
        if (!isValid)
        {
            throw new InvalidOperationException($"Verification failed for {_mod.Name}");
        }

        Log.Information("Verification successful for {ModName}", _mod.Name);
    }

    public async Task CommitAsync()
    {
        if (_tempDirectory == null)
            throw new InvalidOperationException("Transaction not started");

        try
        {
            var targetPath = _installService.GetInstallPath(_mod.Id);
            if (Directory.Exists(targetPath))
            {
                _backupDirectory = targetPath + ".backup";
                if (Directory.Exists(_backupDirectory))
                {
                    Directory.Delete(_backupDirectory, true);
                }
                Directory.Move(targetPath, _backupDirectory);
            }

            Directory.Move(_tempDirectory, targetPath);
            
            _mod.IsInstalled = true;
            _db.Upsert("installed_mods", _mod);

            _isCommitted = true;
            
            if (_backupDirectory != null && Directory.Exists(_backupDirectory))
            {
                Directory.Delete(_backupDirectory, true);
            }

            Log.Information("Transaction committed for {ModName}", _mod.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to commit transaction for {ModName}", _mod.Name);
            throw;
        }
    }

    public async Task RollbackAsync()
    {
        if (_isCommitted) return;

        try
        {
            if (_backupDirectory != null && Directory.Exists(_backupDirectory))
            {
                var targetPath = _installService.GetInstallPath(_mod.Id);
                if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, true);
                }
                Directory.Move(_backupDirectory, targetPath);
            }

            if (_tempDirectory != null && Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }

            Log.Information("Transaction rolled back for {ModName}", _mod.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to rollback transaction for {ModName}", _mod.Name);
        }
    }

    public void Dispose()
    {
        try
        {
            if (_tempDirectory != null && Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }

            if (!_isCommitted && _backupDirectory != null && Directory.Exists(_backupDirectory))
            {
                Directory.Delete(_backupDirectory, true);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to cleanup transaction files");
        }
    }
}