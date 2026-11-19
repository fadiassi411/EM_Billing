using System.Net;

namespace MallEnergyBilling.Web.Services;

public static class FirstRunSetup
{
    /// <summary>True when the request comes from the server computer itself (browser on localhost).</summary>
    public static bool IsLocalRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null) return true; // in-process test server
        if (IPAddress.IsLoopback(remote)) return true;
        return context.Connection.LocalIpAddress is { } local && remote.Equals(local);
    }
}
