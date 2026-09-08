using System.Security.Cryptography;
using Watchdog.Licensing;

namespace MallEnergyBilling.Tests;

public sealed class LicenseDocumentTests
{
    [Fact]
    public void SignedLicense_RoundTripsAndVerifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = new LicensePayload(1, Guid.NewGuid(), "Test Mall", "WD-ABCDE-FGHIJ-KLMNO", "Commercial", 100, DateTimeOffset.UtcNow, null);
        var document = LicenseDocument.Create(payload, key.ExportPkcs8PrivateKeyPem());

        var parsed = LicenseDocument.Parse(document.ToJson());
        var valid = parsed.TryVerify(key.ExportSubjectPublicKeyInfoPem(), out var verified, out var error);

        Assert.True(valid, error);
        Assert.Equal(payload, verified);
    }

    [Fact]
    public void SignedLicense_RejectsModifiedPayload()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = new LicensePayload(1, Guid.NewGuid(), "Test Mall", "WD-ABCDE-FGHIJ-KLMNO", "Commercial", 50, DateTimeOffset.UtcNow, null);
        var document = LicenseDocument.Create(payload, key.ExportPkcs8PrivateKeyPem());
        var replacement = document.Payload[^1] == 'A' ? 'B' : 'A';
        var modified = document with { Payload = document.Payload[..^1] + replacement };

        Assert.False(modified.TryVerify(key.ExportSubjectPublicKeyInfoPem(), out _, out _));
    }

    [Fact]
    public void InstallationIdentity_IsStableAndDoesNotExposeMachineName()
    {
        var first = new MallEnergyBilling.Web.Services.InstallationIdentity().Id;
        var second = new MallEnergyBilling.Web.Services.InstallationIdentity().Id;

        Assert.Equal(first, second);
        Assert.StartsWith("WD-", first);
        Assert.DoesNotContain(Environment.MachineName, first, StringComparison.OrdinalIgnoreCase);
    }
}
