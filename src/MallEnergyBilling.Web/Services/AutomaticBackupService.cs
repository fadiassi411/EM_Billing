using Microsoft.Data.Sqlite;

namespace MallEnergyBilling.Web.Services;

public sealed class AutomaticBackupService(AppDataPaths paths, DatabaseMaintenanceService maintenance, ILogger<AutomaticBackupService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await CreateDailyBackup(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { log.LogError(ex, "Automatic database backup failed"); }
            await Task.Delay(TimeSpan.FromHours(6), ct);
        }
    }
    async Task CreateDailyBackup(CancellationToken ct)
    {
        Directory.CreateDirectory(paths.BackupDirectory);
        var prefix = $"watch-dog-em-auto-{DateTime.Now:yyyyMMdd}";
        if (Directory.EnumerateFiles(paths.BackupDirectory, prefix + "*.db").Any()) return;
        var target = Path.Combine(paths.BackupDirectory, prefix + ".db");
        await maintenance.Gate.WaitAsync(ct);
        try
        {
            await using var source = new SqliteConnection($"Data Source={paths.DatabasePath};Mode=ReadOnly");
            await using var destination = new SqliteConnection($"Data Source={target}");
            await source.OpenAsync(ct); await destination.OpenAsync(ct); source.BackupDatabase(destination);
        }
        finally { maintenance.Gate.Release(); }
        foreach (var old in new DirectoryInfo(paths.BackupDirectory).GetFiles("watch-dog-em-auto-*.db").OrderByDescending(x => x.CreationTimeUtc).Skip(30)) old.Delete();
        log.LogInformation("Automatic database backup created at {Path}", target);
    }
}
