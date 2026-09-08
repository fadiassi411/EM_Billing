using System.Text;
using System.Text.Json;
using Watchdog.Licensing;

namespace MallEnergyBilling.Web.Services;

public sealed record LicenseStatus(string InstallationId, bool IsLicensed, bool IsValid, string Status,
    int MaximumMeters, int ActiveMeters, LicensePayload? License, string? Error)
{
    public int RemainingMeters => Math.Max(0, MaximumMeters - ActiveMeters);
    public bool IsOverLimit => ActiveMeters > MaximumMeters;
}

public sealed class LicenseService(AppDataPaths paths, InstallationIdentity identity)
{
    public const int FreeMeterLimit = 5;
    public const int PaidMeterBlock = 50;
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEGc61/ruNWVnNOOmhewW8JXys8H6w
        201RRW26jMl9q5wYlA99veIVqABB72UgyYY17o7dX+l+xD3UhYrDGDiURw==
        -----END PUBLIC KEY-----
        """;

    public string InstallationId => identity.Id;

    public LicenseStatus GetStatus(int activeMeters)
    {
        if (!File.Exists(paths.LicensePath))
            return new(identity.Id, false, true, "Free", FreeMeterLimit, activeMeters, null, null);
        try
        {
            var document = LicenseDocument.Parse(File.ReadAllText(paths.LicensePath, Encoding.UTF8));
            if (!document.TryVerify(PublicKeyPem, out var payload, out var signatureError) || payload is null)
                return Invalid(activeMeters, signatureError);
            if (!string.Equals(payload.InstallationId, identity.Id, StringComparison.OrdinalIgnoreCase))
                return Invalid(activeMeters, "This license was issued for a different Watchdog installation.", payload);
            if (payload.MaximumMeters < PaidMeterBlock || payload.MaximumMeters % PaidMeterBlock != 0)
                return Invalid(activeMeters, "The licensed meter capacity is invalid.", payload);
            if (payload.ExpiresAtUtc is not null && payload.ExpiresAtUtc.Value < DateTimeOffset.UtcNow)
                return new(identity.Id, true, false, "Expired", FreeMeterLimit, activeMeters, payload, "This license has expired.");
            return new(identity.Id, true, true, "Licensed", payload.MaximumMeters, activeMeters, payload, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return Invalid(activeMeters, "The installed license cannot be read or verified.");
        }
    }

    public bool CanActivateMeter(int currentActiveMeters) => currentActiveMeters < GetStatus(currentActiveMeters).MaximumMeters;

    public async Task<LicenseStatus> ImportAsync(Stream source, int activeMeters, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(source, Encoding.UTF8, true, 1024, leaveOpen: true);
        var json = await reader.ReadToEndAsync(cancellationToken);
        if (Encoding.UTF8.GetByteCount(json) > 64 * 1024) throw new InvalidDataException("The license file is too large.");
        LicenseDocument document;
        try { document = LicenseDocument.Parse(json); }
        catch { throw new InvalidDataException("Select a valid .wdlicense file."); }
        if (!document.TryVerify(PublicKeyPem, out var payload, out var error) || payload is null) throw new InvalidDataException(error);
        if (!string.Equals(payload.InstallationId, identity.Id, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("This license belongs to a different Watchdog installation.");
        if (payload.MaximumMeters < PaidMeterBlock || payload.MaximumMeters % PaidMeterBlock != 0) throw new InvalidDataException("The license meter capacity must be a multiple of 50.");
        if (string.IsNullOrWhiteSpace(payload.CustomerName)) throw new InvalidDataException("The license customer name is missing.");
        if (payload.ExpiresAtUtc is not null && payload.ExpiresAtUtc.Value < DateTimeOffset.UtcNow) throw new InvalidDataException("This license has expired.");

        Directory.CreateDirectory(paths.DataDirectory);
        var temporaryPath = paths.LicensePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, document.ToJson(), Encoding.UTF8, cancellationToken);
        File.Move(temporaryPath, paths.LicensePath, overwrite: true);
        return GetStatus(activeMeters);
    }

    public void Remove() { if (File.Exists(paths.LicensePath)) File.Delete(paths.LicensePath); }

    private LicenseStatus Invalid(int activeMeters, string error, LicensePayload? payload = null) =>
        new(identity.Id, payload is not null, false, "Invalid", FreeMeterLimit, activeMeters, payload, error);
}
