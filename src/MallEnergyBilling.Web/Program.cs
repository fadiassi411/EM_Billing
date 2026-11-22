using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "Watchdog Energy Management Server");
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var configuredDataDirectory = builder.Configuration["Storage:DataDirectory"];
var commonDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
var dataDirectory = !string.IsNullOrWhiteSpace(configuredDataDirectory)
    ? Environment.ExpandEnvironmentVariables(configuredDataDirectory)
    : builder.Environment.IsDevelopment()
        ? builder.Environment.ContentRootPath
        : Path.Combine(commonDataDirectory, "Watch Dog EM");
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(configuredDataDirectory))
    MigratePreviousBrandData(commonDataDirectory, dataDirectory);
Directory.CreateDirectory(dataDirectory);
var appDataPaths = new AppDataPaths(dataDirectory);
builder.Services.AddSingleton(appDataPaths);
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "Keys")))
    .SetApplicationName("WatchDogEM");
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
builder.Logging.AddProvider(new DailyFileLoggerProvider(Path.Combine(dataDirectory, "Logs")));
var cs = $"Data Source={appDataPaths.DatabasePath};Cache=Shared";
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(cs));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddDefaultIdentity<IdentityUser>(o =>
{
    o.SignIn.RequireConfirmedAccount = false;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Password.RequiredLength = 10;
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.ConfigureApplicationCookie(o => o.ExpireTimeSpan = TimeSpan.FromMinutes(30));
builder.Services.AddRazorPages(o =>
{
    // Each area needs the matching permission. Administrators always pass; other levels get
    // their permissions from Settings > Users & access > User levels.
    o.Conventions.AuthorizeFolder("/Operations", Permissions.PolicyName(Permissions.ViewOperations));
    o.Conventions.AuthorizeFolder("/PowerSources", Permissions.PolicyName(Permissions.ViewMeters));
    o.Conventions.AuthorizeFolder("/Water", Permissions.PolicyName(Permissions.ViewMeters));
    o.Conventions.AuthorizePage("/Meters/Details", Permissions.PolicyName(Permissions.ViewMeters));
    o.Conventions.AuthorizePage("/Meters/Review", Permissions.PolicyName(Permissions.ReviewCounters));
    o.Conventions.AuthorizeFolder("/Invoices", Permissions.PolicyName(Permissions.ViewInvoices));
    o.Conventions.AuthorizeFolder("/Admin/Controllers", Permissions.PolicyName(Permissions.ManageControllers));
    o.Conventions.AuthorizeFolder("/Admin/Shops", Permissions.PolicyName(Permissions.ManageShops));
    o.Conventions.AuthorizeFolder("/Admin/Audit", Permissions.PolicyName(Permissions.ViewAudit));
    o.Conventions.AuthorizeFolder("/Admin/Email", Permissions.PolicyName(Permissions.ManageEmailSettings));
    o.Conventions.AuthorizeFolder("/Admin/Features", Permissions.PolicyName(Permissions.ManageFeatures));
    o.Conventions.AuthorizeFolder("/Admin/License", Permissions.PolicyName(Permissions.ManageLicense));
    // Users and user levels stay with Administrators, so nobody can give themselves more rights.
    o.Conventions.AuthorizeFolder("/Admin/Users", "AdministratorOnly");
}).AddMvcOptions(o =>
    // Only fields marked [Required] are required. Without this, every optional text box
    // (Reason, Tenant, a hidden BACnet field on a Modbus form, ...) was rejected when left empty.
    o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("AdministratorOnly", p => p.RequireRole("Administrator"));
    foreach (var permission in Permissions.All)
        o.AddPolicy(Permissions.PolicyName(permission.Key), p => p.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Key)));
    // Every page requires a signed-in user unless it is explicitly marked [AllowAnonymous] (login pages).
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});
var clockOffsetDays = builder.Configuration.GetValue<double?>("Testing:ClockOffsetDays");
if (builder.Environment.IsDevelopment() && clockOffsetDays is double offsetDays && offsetDays != 0)
    builder.Services.AddSingleton<TimeProvider>(new OffsetTimeProvider(TimeSpan.FromDays(offsetDays)));
else
    builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<BillingRunService>();
builder.Services.AddSingleton<PermissionService>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionHandler>();
// A changed access level reaches signed-in users within a minute.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));
builder.Services.AddSingleton<BillingCalculator>();
builder.Services.AddSingleton<TariffResolver>();
builder.Services.AddSingleton<IModbusService, ModbusService>();
builder.Services.AddSingleton<IBacnetTransport, BacnetTransport>();
builder.Services.AddSingleton<IBacnetIpService, BacnetIpService>();
builder.Services.AddHostedService<BacnetPollingService>();
builder.Services.AddSingleton<InvoicePdfService>();
builder.Services.AddScoped<InvoiceEmailService>();
builder.Services.AddScoped<SystemFeatureService>();
builder.Services.AddSingleton<DatabaseMaintenanceService>();
builder.Services.AddSingleton<InstallationIdentity>();
builder.Services.AddSingleton<LicenseService>();
builder.Services.AddHostedService<MeterPollingService>();
builder.Services.AddHostedService<AutomaticBackupService>();
builder.Services.AddHostedService<InvoiceSchedulerService>();
builder.Services.AddHostedService<AutomaticInvoiceEmailService>();

var app = builder.Build();
if (app.Services.GetRequiredService<TimeProvider>() is OffsetTimeProvider testClock)
    app.Logger.LogWarning("TEST CLOCK ACTIVE: the application clock is shifted by {Offset}. Never use Testing:ClockOffsetDays on a live installation.", testClock.Offset);
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error"); app.UseHsts(); }
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/Identity/Account/Register") || context.Request.Path.StartsWithSegments("/Admin/Bootstrap"))
    {
        using var scope = context.RequestServices.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        if ((await users.GetUsersInRoleAsync("Administrator")).Any())
        {
            if (context.Request.Path.StartsWithSegments("/Identity/Account/Register"))
            {
                context.Response.Redirect(context.User.IsInRole("Administrator")
                    ? "/Admin/Users"
                    : "/Identity/Account/Login");
                return;
            }
        }
        else if (!FirstRunSetup.IsLocalRequest(context))
        {
            // Until the first Administrator exists, setup is only allowed on the server computer itself,
            // so nobody else on the network can register first and take over the system.
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("First-time setup must be done on the server computer. Open http://localhost:5080 on the Watchdog server to create the first Administrator.");
            return;
        }
    }
    await next();
});
app.MapRazorPages();
using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await SeedData.InitializeAsync(database);
    var smtpConfiguration = await database.SmtpConfigurations.FindAsync(1);
    if (smtpConfiguration?.FromName == "Watch Dog EM")
    {
        smtpConfiguration.FromName = "Watchdog Energy Management";
        await database.SaveChangesAsync();
    }
    var firstMeterId = await database.Meters.OrderBy(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync();
    if (firstMeterId is not null)
    {
        var legacyTariffs = await database.Tariffs.Where(x => x.MeterId == null).ToListAsync();
        foreach (var tariff in legacyTariffs) tariff.MeterId = firstMeterId;
        if (legacyTariffs.Count > 0) await database.SaveChangesAsync();
    }
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Administrator", "BillingManager", "Operator", "Viewer" })
        if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));
    await app.Services.GetRequiredService<PermissionService>().SeedDefaultsAsync(roles);
}
app.Run();

static void MigratePreviousBrandData(string commonDataDirectory, string destinationDirectory)
{
    // Keep version 1.0 customer data during the Watchdog Energy Management rebrand.
    var previousDirectory = Path.Combine(commonDataDirectory, string.Concat("Black", "Dog EM"));
    var previousDatabase = Path.Combine(previousDirectory, "app.db");
    var destinationDatabase = Path.Combine(destinationDirectory, "app.db");
    if (!File.Exists(previousDatabase) || File.Exists(destinationDatabase)) return;

    try
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(previousDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(previousDirectory, sourceFile);
            var destinationFile = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: false);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Customer data migration could not be completed: {ex.Message}");
    }
}

public partial class Program { }
