using Microsoft.EntityFrameworkCore;

namespace MallEnergyBilling.Web.Data;

public static class SeedData
{
    public static Task InitializeAsync(ApplicationDbContext db) => db.Database.MigrateAsync();
}
