using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Pages.Admin.Backup;

/// <summary>Backup & restore (needs the "Backup & restore" permission; checked by the folder policy).</summary>
public sealed class IndexModel(ApplicationDbContext db, AppDataPaths paths, DatabaseMaintenanceService maintenance) : PageModel
{
    public string BackupPath => paths.BackupDirectory;
    public List<BackupFile> BackupFiles { get; set; } = [];
    public sealed record BackupFile(string Name, long Size, DateTime Modified)
    {
        public string Kind => Name.Contains("-auto-") ? "Automatic" : Name.Contains("before-") ? "Safety copy" : "Manual";
    }

    public void OnGet() => LoadBackups();

    public async Task<IActionResult> OnPostBackup()
    {
        await maintenance.Gate.WaitAsync(HttpContext.RequestAborted);
        try
        {
            await db.Database.CloseConnectionAsync();
            TempData["BackupSuccess"] = $"Backup created: {await BackupToAsync($"watch-dog-em-{DateTime.Now:yyyyMMdd-HHmmss}.db")}";
        }
        finally { maintenance.Gate.Release(); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestore(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName) { TempData["BackupError"] = "Invalid backup file."; return RedirectToPage(); }
        var backupPath = Path.Combine(BackupPath, fileName);
        if (!System.IO.File.Exists(backupPath)) { TempData["BackupError"] = "Backup file was not found."; return RedirectToPage(); }
        await maintenance.Gate.WaitAsync(HttpContext.RequestAborted);
        try
        {
            await db.Database.CloseConnectionAsync();
            SqliteConnection.ClearAllPools();
            await using (var source = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly"))
            {
                await source.OpenAsync();
                await using (var check = source.CreateCommand())
                {
                    check.CommandText = "PRAGMA quick_check;";
                    if (!string.Equals((string?)await check.ExecuteScalarAsync(), "ok", StringComparison.OrdinalIgnoreCase))
                    { TempData["BackupError"] = "The selected backup failed its integrity check."; return RedirectToPage(); }
                }
                // Keep a copy of the current data first, so a wrong restore can be undone.
                var safety = await BackupToAsync($"watch-dog-em-before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db");
                maintenance.DatabaseReplaced();
                await using var destination = new SqliteConnection($"Data Source={paths.DatabasePath}");
                await destination.OpenAsync();
                source.BackupDatabase(destination);
                TempData["BackupSuccess"] = $"Restored {fileName}. The data from before the restore was saved as {safety}.";
            }
            SqliteConnection.ClearAllPools();
            // A backup from an older version is upgraded to the current database structure straight away.
            await db.Database.MigrateAsync();
            maintenance.DatabaseReplaced();
        }
        finally { maintenance.Gate.Release(); }
        return RedirectToPage();
    }

    async Task<string> BackupToAsync(string name)
    {
        Directory.CreateDirectory(BackupPath);
        await using var source = new SqliteConnection($"Data Source={paths.DatabasePath};Mode=ReadOnly");
        await using var destination = new SqliteConnection($"Data Source={Path.Combine(BackupPath, name)}");
        await source.OpenAsync(); await destination.OpenAsync();
        source.BackupDatabase(destination);
        return name;
    }

    void LoadBackups()
    {
        Directory.CreateDirectory(BackupPath);
        BackupFiles = new DirectoryInfo(BackupPath).GetFiles("*.db").OrderByDescending(x => x.LastWriteTimeUtc).Select(x => new BackupFile(x.Name, x.Length, x.LastWriteTime)).ToList();
    }
}
