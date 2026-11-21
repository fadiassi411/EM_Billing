using System.Security.Claims;
using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MallEnergyBilling.Tests;

public sealed class PermissionTests : IAsyncLifetime
{
    readonly SqliteConnection connection = new("Data Source=:memory:");
    ServiceProvider provider = null!;
    PermissionService permissions = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection));
        services.AddIdentityCore<IdentityUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddSingleton<PermissionService>();
        provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Permissions.BuiltInRoles) await roles.CreateAsync(new IdentityRole(role));
        permissions = provider.GetRequiredService<PermissionService>();
        await permissions.SeedDefaultsAsync(roles);
    }

    public async Task DisposeAsync() { await provider.DisposeAsync(); await connection.DisposeAsync(); }

    static ClaimsPrincipal UserWith(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)).Append(new Claim(ClaimTypes.Name, "test")), "Test"));

    async Task Set(string role, params string[] granted)
    {
        using var scope = provider.CreateScope();
        await permissions.SetAsync(scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(), role, granted);
    }

    [Fact] public async Task BuiltInLevelsKeepTheirPreviousRights()
    {
        Assert.True(await permissions.HasAsync(UserWith("BillingManager"), Permissions.RunBilling));
        Assert.True(await permissions.HasAsync(UserWith("BillingManager"), Permissions.RecordPayments));
        Assert.False(await permissions.HasAsync(UserWith("BillingManager"), Permissions.ManageControllers));
        Assert.True(await permissions.HasAsync(UserWith("Operator"), Permissions.ExportReadings));
        Assert.False(await permissions.HasAsync(UserWith("Operator"), Permissions.RunBilling));
        Assert.True(await permissions.HasAsync(UserWith("Viewer"), Permissions.ViewMeters));
        Assert.False(await permissions.HasAsync(UserWith("Viewer"), Permissions.ViewOperations));
    }

    [Fact] public async Task AdministratorAlwaysHasEverythingAndCannotBeEdited()
    {
        foreach (var permission in Permissions.All)
            Assert.True(await permissions.HasAsync(UserWith(Permissions.AdministratorRole), permission.Key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Set(Permissions.AdministratorRole));
    }

    [Fact] public async Task SignedOutOrUnknownLevelHasNothing()
    {
        Assert.False(await permissions.HasAsync(new ClaimsPrincipal(new ClaimsIdentity()), Permissions.ViewDashboard));
        Assert.False(await permissions.HasAsync(UserWith("NoSuchLevel"), Permissions.ViewDashboard));
    }

    [Fact] public async Task TickedBoxesApplyImmediately()
    {
        Assert.False(await permissions.HasAsync(UserWith("Viewer"), Permissions.ViewAudit));
        await Set("Viewer", Permissions.ViewDashboard, Permissions.ViewAudit);
        Assert.True(await permissions.HasAsync(UserWith("Viewer"), Permissions.ViewAudit));
        Assert.False(await permissions.HasAsync(UserWith("Viewer"), Permissions.ViewMeters)); // unticked
    }

    [Fact] public async Task AnActionAlsoGrantsThePageItLivesOn()
    {
        await Set("Viewer", Permissions.RecordPayments, Permissions.ReviewCounters);
        var granted = await permissions.ForRoleAsync("Viewer");
        Assert.Contains(Permissions.ViewOperations, granted);
        Assert.Contains(Permissions.ViewMeters, granted);
    }

    [Fact] public async Task AdministratorChoicesSurviveARestart()
    {
        await Set("Operator"); // everything unticked on purpose
        using var scope = provider.CreateScope();
        await permissions.SeedDefaultsAsync(scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
        Assert.Empty(await permissions.ForRoleAsync("Operator"));
    }

    [Fact] public async Task NewLevelUsesOnlyItsTickedPermissions()
    {
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>().CreateAsync(new IdentityRole("Technician"));
        await Set("Technician", Permissions.ViewMeters, Permissions.ManageControllers);
        var technician = UserWith("Technician");
        Assert.True(await permissions.HasAsync(technician, Permissions.ManageControllers));
        Assert.False(await permissions.HasAsync(technician, Permissions.ViewInvoices));
    }

    [Fact] public async Task PolicyHandlerFollowsThePermissions()
    {
        var handler = new PermissionHandler(permissions);
        var requirement = new PermissionRequirement(Permissions.RunBilling);
        var allowed = new AuthorizationHandlerContext([requirement], UserWith("BillingManager"), null);
        await handler.HandleAsync(allowed);
        Assert.True(allowed.HasSucceeded);
        var denied = new AuthorizationHandlerContext([requirement], UserWith("Viewer"), null);
        await handler.HandleAsync(denied);
        Assert.False(denied.HasSucceeded);
    }
}
