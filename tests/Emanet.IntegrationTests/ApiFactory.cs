using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Emanet.Api.Features.Bookings;
using Emanet.Core.Payments;
using Emanet.Infrastructure.Payments;
using Emanet.Infrastructure.Storage;
using Emanet.IntegrationTests.Support;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Emanet.IntegrationTests;

/// <summary>Boots the real API against a throwaway PostgreSQL, shared by the whole collection, on a clock tests can move.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-tests-signing-key-0123456789abcdef";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgis/postgis:17-3.5-alpine").Build();

    /// <summary>Tests run one after another in this collection, so moving the clock affects only the test that does it.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public string ConnectionString => _database.GetConnectionString();

    /// <summary>Photos are kept in memory here; <see cref="StorageTests"/> runs the real store against MinIO.</summary>
    public MemoryFileStore Files { get; } = new();

    public const string AdminEmail = "books@example.com";

    /// <summary>A second administrator, for the claims tests.</summary>
    public const string SupportEmail = "support@example.com";

    /// <summary>The payment provider every request uses; tests can make it fail.</summary>
    public FlakyPayments Payments => Services.GetRequiredService<FlakyPayments>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Emanet", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Auth:AttemptsPerMinute", "100000");
        builder.UseSetting("Storage:ServiceUrl", "http://storage.invalid");
        builder.UseSetting("Storage:AccessKey", "not-used");
        builder.UseSetting("Storage:SecretKey", "not-used");
        // Tests run the sweeper themselves, one pass at a time.
        builder.UseSetting("Sweeper:Enabled", "false");
        builder.UseSetting("Admin:Emails:0", AdminEmail);
        builder.UseSetting("Admin:Emails:1", SupportEmail);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IFileStore>();
            services.AddSingleton<IFileStore>(Files);
            services.AddSingleton<FlakyPayments>();
            services.RemoveAll<IPaymentProvider>();
            services.AddSingleton<IPaymentProvider>(provider => provider.GetRequiredService<FlakyPayments>());
        });
    }

    public Task<SweepPass> SweepAsync() => Services.GetRequiredService<BookingSweeper>().RunOnceAsync();

    /// <summary>Today in Turkey on the test clock, plus some days.</summary>
    public DateOnly Day(int fromToday) => Core.Clock.Today(Clock.GetUtcNow()).AddDays(fromToday);

    public async Task<T?> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        for (var i = 0; i < parameters.Length; i++)
        {
            command.Parameters.AddWithValue($"p{i}", parameters[i]);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        CreateClient().Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
