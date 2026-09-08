using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace MallEnergyBilling.Web.Services;

public sealed class InstallationIdentity
{
    private readonly Lazy<string> installationId = new(CreateInstallationId);
    public string Id => installationId.Value;

    private static string CreateInstallationId()
    {
        var source = ReadWindowsMachineGuid();
        if (string.IsNullOrWhiteSpace(source))
            source = $"{Environment.MachineName}|{Environment.OSVersion.Platform}|{Environment.Is64BitOperatingSystem}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"WatchdogEnergyManagement|{source.Trim()}"));
        return "WD-" + string.Join('-', Convert.ToHexString(hash[..15]).Chunk(5).Select(x => new string(x)));
    }

    private static string? ReadWindowsMachineGuid()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid")?.ToString();
        }
        catch { return null; }
    }
}
