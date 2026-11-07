using Emanet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Emanet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddEmanetPersistence(this IServiceCollection services, string connectionString)
    {
        var dataSource = BuildDataSource(connectionString);
        services.AddSingleton(dataSource);
        services.AddDbContext<EmanetDbContext>(options => ConfigureDbContext(options, dataSource));
        return services;
    }

    public static NpgsqlDataSource BuildDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        // Points travel as PostGIS geography, read and written as NetTopologySuite geometries.
        builder.UseNetTopologySuite();
        return builder.Build();
    }

    /// <summary>
    /// Brings the schema up to date. Several instances start at once in Compose (and in any scaled deployment), so
    /// they take turns behind a PostgreSQL advisory lock: the first migrates, the others find nothing left to do.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        const long lockKey = 0x456D616E6574; // "Emanet"
        await using var connection = await services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(ct);
        await using (var take = new NpgsqlCommand($"SELECT pg_advisory_lock({lockKey})", connection))
        {
            await take.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<EmanetDbContext>().Database.MigrateAsync(ct);
        }
        finally
        {
            await using var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({lockKey})", connection);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    internal static DbContextOptionsBuilder ConfigureDbContext(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history").UseNetTopologySuite())
            .UseSnakeCaseNamingConvention();
}
