using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "Watch Dog EM Server");
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
    o.Conventions.AuthorizeFolder("/Operations");
    o.Conventions.AuthorizeFolder("/PowerSources");
    o.Conventions.AuthorizeFolder("/Admin/Controllers", "AdministratorOnly");
    o.Conventions.AuthorizeFolder("/Admin/Shops", "AdministratorOnly");
    o.Conventions.AuthorizeFolder("/Admin/Audit", "AdministratorOnly");
    o.Conventions.AuthorizeFolder("/Admin/Users", "AdministratorOnly");
    o.Conventions.AuthorizeFolder("/Admin/Email", "AdministratorOnly");
});
builder.Services.AddAuthorization(o => o.AddPolicy("AdministratorOnly", p => p.RequireRole("Administrator")));
builder.Services.AddSingleton<BillingCalculator>();
builder.Services.AddSingleton<TariffResolver>();
builder.Services.AddSingleton<IModbusService, ModbusService>();
builder.Services.AddSingleton<InvoicePdfService>();
builder.Services.AddScoped<InvoiceEmailService>();
builder.Services.AddSingleton<DatabaseMaintenanceService>();
builder.Services.AddHostedService<MeterPollingService>();
builder.Services.AddHostedService<AutomaticBackupService>();
builder.Services.AddHostedService<InvoiceSchedulerService>();
builder.Services.AddHostedService<AutomaticInvoiceEmailService>();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error"); app.UseHsts(); }
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/Identity/Account/Register"))
    {
        using var scope = context.RequestServices.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        if ((await users.GetUsersInRoleAsync("Administrator")).Any())
        {
            context.Response.Redirect(context.User.IsInRole("Administrator")
                ? "/Admin/Users"
                : "/Identity/Account/Login");
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
}
app.Run();

static void MigratePreviousBrandData(string commonDataDirectory, string destinationDirectory)
{
    // Keep version 1.0 customer data during the Watch Dog EM rebrand.
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
