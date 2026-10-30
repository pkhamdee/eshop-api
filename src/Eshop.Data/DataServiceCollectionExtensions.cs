using Eshop.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Eshop.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EshopDbContext"/> on PostgreSQL. Shared by every .NET service.
    /// Demo data is inserted by EF Core's seeding hook when migrations are applied.
    /// </summary>
    public static IServiceCollection AddEshopData(this IServiceCollection services, string connectionString) =>
        services.AddDbContext<EshopDbContext>(options => Configure(options, connectionString));

    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .UseSeeding((db, _) => DemoData.Seed(db))
            .UseAsyncSeeding((db, _, ct) => DemoData.SeedAsync(db, ct));
}
