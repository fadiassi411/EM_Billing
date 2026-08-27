namespace MallEnergyBilling.Web.Services;

public sealed record AppDataPaths(string DataDirectory)
{
    public string DatabasePath => Path.Combine(DataDirectory, "app.db");
    public string BackupDirectory => Path.Combine(DataDirectory, "Backups");
}
