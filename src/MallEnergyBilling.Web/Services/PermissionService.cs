using System.Collections.Concurrent;
using System.Security.Claims;
using MallEnergyBilling.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Services;

public sealed record PermissionDefinition(string Key, string Group, string Name, string Description);

/// <summary>Everything a user level can be allowed to see or do. Administrator always has all of them.</summary>
public static class Permissions
{
    public const string ViewDashboard = "dashboard.view";
    public const string ViewMeters = "meters.view";
    public const string ViewInvoices = "invoices.view";
    public const string ViewOperations = "operations.view";
    public const string ExportReadings = "readings.export";
    public const string ManageTariffs = "billing.tariffs";
    public const string RunBilling = "billing.run";
    public const string RecordPayments = "billing.payments";
    public const string EmailInvoices = "invoices.email";
    public const string ReviewCounters = "meters.review";
    public const string ManageShops = "settings.shops";
    public const string ManageControllers = "settings.controllers";
    public const string ManageEmailSettings = "settings.email";
    public const string ManageFeatures = "settings.features";
    public const string ManageLicense = "settings.license";
    public const string ViewAudit = "settings.audit";
    public const string BackupRestore = "settings.backup";

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(ViewDashboard, "Monitor", "Dashboard", "See the dashboard: meter status, totals, outstanding balance."),
        new(ViewMeters, "Monitor", "Meters", "See Grid, Generator and Water meters, meter details and charts."),
        new(ViewInvoices, "Monitor", "Invoices", "See published invoices and download invoice PDFs."),
        new(ViewOperations, "Monitor", "Billing & operations page", "Open the Billing & operations page (readings, meter register, schedule)."),
        new(ExportReadings, "Monitor", "Export readings", "Download all meter readings as a CSV file."),
        new(ManageTariffs, "Billing", "Tariffs & billing cycle", "Change meter tariffs, the billing cycle start day and publication day."),
        new(RunBilling, "Billing", "Billing runs & preview", "Preview invoices, generate and finalize billing periods."),
        new(RecordPayments, "Billing", "Record payments", "Record customer payments against invoices."),
        new(EmailInvoices, "Billing", "Email invoices", "Send invoice PDFs to customers by email."),
        new(ReviewCounters, "Control", "Counter review", "Ignore or accept counter decreases and mark meters as replaced."),
        new(ManageShops, "Control", "Shops", "Add and edit shops and tenant details."),
        new(ManageControllers, "Control", "Controllers & meters", "Add, edit, archive and delete controllers, channels, meters and BACnet points."),
        new(ManageEmailSettings, "Settings", "Invoice email settings", "Change SMTP settings and automatic invoice email."),
        new(ManageFeatures, "Settings", "System features", "Turn optional features (such as water billing) on or off."),
        new(ManageLicense, "Settings", "License", "Import or remove the software license."),
        new(ViewAudit, "Settings", "Audit trail", "See the audit trail of all changes."),
        new(BackupRestore, "Settings", "Backup & restore", "Create backups and restore the database from a backup."),
    ];

    public const string AdministratorRole = "Administrator";
    public static readonly string[] BuiltInRoles = [AdministratorRole, "BillingManager", "Operator", "Viewer"];

    /// <summary>What the built-in levels could do before levels became configurable (used once, on first start).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Defaults = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["BillingManager"] = [ViewDashboard, ViewMeters, ViewInvoices, ViewOperations, ExportReadings, ManageTariffs, RunBilling, RecordPayments, EmailInvoices],
        ["Operator"] = [ViewDashboard, ViewMeters, ViewInvoices, ViewOperations, ExportReadings],
        ["Viewer"] = [ViewDashboard, ViewMeters, ViewInvoices],
    };

    /// <summary>Permissions that are only usable together with another one (the page they live on).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Requires = new Dictionary<string, string[]>
    {
        [ExportReadings] = [ViewOperations],
        [ManageTariffs] = [ViewOperations],
        [RunBilling] = [ViewOperations],
        [RecordPayments] = [ViewOperations],
        [EmailInvoices] = [ViewInvoices],
        [ReviewCounters] = [ViewMeters],
    };

    /// <summary>Adds the pages a ticked permission needs, so a level never gets a button it cannot reach.</summary>
    public static HashSet<string> WithRequired(IEnumerable<string> permissions)
    {
        var result = permissions.ToHashSet();
        foreach (var permission in result.ToArray())
            if (Requires.TryGetValue(permission, out var needed)) result.UnionWith(needed);
        return result;
    }

    public static string PolicyName(string permission) => "Permission:" + permission;

    public static string DisplayName(string role) => role switch
    {
        "BillingManager" => "Billing Manager",
        _ => role
    };
}

/// <summary>
/// Reads and stores level permissions as Identity role claims ("permission" claims on AspNetRoleClaims).
/// Results are cached and refreshed as soon as an Administrator saves a change, so a new tick or
/// untick applies on the user's next click without signing in again.
/// </summary>
public sealed class PermissionService(IServiceScopeFactory scopes)
{
    public const string PermissionClaim = "permission";
    public const string ConfiguredClaim = "permissions-configured";
    private volatile IReadOnlyDictionary<string, HashSet<string>>? cache;
    private readonly SemaphoreSlim loadGate = new(1, 1);

    public void Invalidate() => cache = null;

    public async Task<IReadOnlyDictionary<string, HashSet<string>>> GetAllAsync()
    {
        if (cache is { } cached) return cached;
        await loadGate.WaitAsync();
        try
        {
            if (cache is { } again) return again;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await (from claim in db.RoleClaims
                              join role in db.Roles on claim.RoleId equals role.Id
                              where claim.ClaimType == PermissionClaim
                              select new { role.Name, claim.ClaimValue }).ToListAsync();
            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (row.Name is null || row.ClaimValue is null) continue;
                if (!map.TryGetValue(row.Name, out var set)) map[row.Name] = set = new(StringComparer.Ordinal);
                set.Add(row.ClaimValue);
            }
            cache = map;
            return map;
        }
        finally { loadGate.Release(); }
    }

    public async Task<bool> HasAsync(ClaimsPrincipal user, string permission)
    {
        if (user.Identity?.IsAuthenticated != true) return false;
        if (user.IsInRole(Permissions.AdministratorRole)) return true;
        var map = await GetAllAsync();
        return user.FindAll(user.Identities.FirstOrDefault()?.RoleClaimType ?? ClaimTypes.Role)
            .Any(role => map.TryGetValue(role.Value, out var set) && set.Contains(permission));
    }

    public async Task<IReadOnlySet<string>> ForRoleAsync(string role)
    {
        if (string.Equals(role, Permissions.AdministratorRole, StringComparison.OrdinalIgnoreCase))
            return Permissions.All.Select(x => x.Key).ToHashSet();
        var map = await GetAllAsync();
        return map.TryGetValue(role, out var set) ? set : new HashSet<string>();
    }

    /// <summary>Replaces the permissions of one level. The Administrator level cannot be changed.</summary>
    public async Task SetAsync(RoleManager<IdentityRole> roles, string roleName, IEnumerable<string> permissions)
    {
        if (string.Equals(roleName, Permissions.AdministratorRole, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Administrator level always has every permission.");
        var role = await roles.FindByNameAsync(roleName) ?? throw new InvalidOperationException($"Level {roleName} was not found.");
        var valid = Permissions.All.Select(x => x.Key).ToHashSet();
        var wanted = Permissions.WithRequired(permissions.Where(valid.Contains));
        var claims = await roles.GetClaimsAsync(role);
        foreach (var claim in claims.Where(c => c.Type == PermissionClaim && !wanted.Contains(c.Value)))
            await roles.RemoveClaimAsync(role, claim);
        var existing = claims.Where(c => c.Type == PermissionClaim).Select(c => c.Value).ToHashSet();
        foreach (var permission in wanted.Where(p => !existing.Contains(p)))
            await roles.AddClaimAsync(role, new Claim(PermissionClaim, permission));
        if (!claims.Any(c => c.Type == ConfiguredClaim))
            await roles.AddClaimAsync(role, new Claim(ConfiguredClaim, "1"));
        Invalidate();
    }

    /// <summary>On first start after the upgrade, give the built-in levels the rights they had before.</summary>
    public async Task SeedDefaultsAsync(RoleManager<IdentityRole> roles)
    {
        foreach (var (roleName, defaults) in Permissions.Defaults)
        {
            var role = await roles.FindByNameAsync(roleName);
            if (role is null) continue;
            var claims = await roles.GetClaimsAsync(role);
            if (claims.Any(c => c.Type == ConfiguredClaim)) continue;
            await SetAsync(roles, roleName, defaults);
        }
        Invalidate();
    }
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionHandler(PermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (await permissions.HasAsync(context.User, requirement.Permission)) context.Succeed(requirement);
    }
}
