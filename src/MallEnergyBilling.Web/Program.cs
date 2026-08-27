using MallEnergyBilling.Web.Data;
using MallEnergyBilling.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var configuredDataDirectory = builder.Configuration["Storage:DataDirectory"];
var dataDirectory = !string.IsNullOrWhiteSpace(configuredDataDirectory)
    ? Environment.ExpandEnvironmentVariables(configuredDataDirectory)
    : builder.Environment.IsDevelopment()
        ? builder.Environment.ContentRootPath
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlackDog EM");
Directory.CreateDirectory(dataDirectory);
var appDataPaths = new AppDataPaths(dataDirectory);
builder.Services.AddSingleton(appDataPaths);
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
    o.Conventions.AuthorizeFolder("/Admin/Controllers", "AdministratorOnly");
    o.Conventions.AuthorizeFolder("/Admin/Shops", "AdministratorOnly");
    o.Conventions.AuthorizeFolder("/Admin/Audit", "AdministratorOnly");
});
builder.Services.AddAuthorization(o => o.AddPolicy("AdministratorOnly", p => p.RequireRole("Administrator")));
builder.Services.AddSingleton<BillingCalculator>();
builder.Services.AddSingleton<TariffResolver>();
builder.Services.AddSingleton<IModbusRtuService, ModbusRtuService>();
builder.Services.AddSingleton<InvoicePdfService>();
builder.Services.AddSingleton<DatabaseMaintenanceService>();
builder.Services.AddHostedService<MeterPollingService>();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error"); app.UseHsts(); }
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapRazorPages();
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
public partial class Program { }
