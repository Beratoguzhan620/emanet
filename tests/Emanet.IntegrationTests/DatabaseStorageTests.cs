using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Emanet.Infrastructure.Storage;
using Testcontainers.PostgreSql;

namespace Emanet.IntegrationTests;

/// <summary>The store that keeps photos in the application's database, the way the one-container demo runs it.</summary>
public sealed class DatabaseStorageTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgis/postgis:17-3.5-alpine").Build();
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Postgres",
        }).Build();
        _services = new ServiceCollection().AddEmanetStorage(configuration, _database.GetConnectionString()).BuildServiceProvider();
        await StorageSetup.InitializeAsync(_services);
        // A second start-up finds the table and leaves it be.
        await StorageSetup.InitializeAsync(_services);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Files_go_in_and_come_back_byte_for_byte()
    {
        var store = _services.GetRequiredService<IFileStore>();
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();

        await store.PutAsync("listings/a/photo.jpg", bytes, "image/jpeg", CancellationToken.None);
        // Sent again, as a device retrying an upload does: the same photo, still one row.
        await store.PutAsync("listings/a/photo.jpg", bytes, "image/jpeg", CancellationToken.None);

        Assert.Equal(bytes, await store.GetAsync("listings/a/photo.jpg", CancellationToken.None));
        Assert.Null(await store.GetAsync("listings/a/missing.jpg", CancellationToken.None));

        await store.DeleteAsync("listings/a/photo.jpg", CancellationToken.None);
        // Removing what is not there is not an error.
        await store.DeleteAsync("listings/a/photo.jpg", CancellationToken.None);
        Assert.Null(await store.GetAsync("listings/a/photo.jpg", CancellationToken.None));
    }

    [Fact]
    public void Without_the_database_the_store_cannot_be_set_up()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Provider"] = "Postgres" }).Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddEmanetStorage(configuration));
    }
}
