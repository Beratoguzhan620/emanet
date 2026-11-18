using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Emanet.Api.Features.Demo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Emanet.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DemoTests(ApiFactory factory)
{
    [Fact]
    public async Task Outside_the_public_demo_there_are_no_demo_sign_ins()
    {
        using var anonymous = factory.CreateClient();

        var accounts = await anonymous.GetFromJsonAsync<List<JsonElement>>("/api/demo");

        Assert.NotNull(accounts);
        Assert.Empty(accounts);
    }

    [Fact]
    public async Task The_demo_neighbourhood_is_made_once_with_photos_reviews_and_a_request_to_answer()
    {
        var demo = new DemoOptions { Enabled = true, PhotosPath = PhotosFolder() };
        var worker = new DemoWorker(Options.Create(demo), new TestClients(factory), factory.Services.GetRequiredService<IHostApplicationLifetime>(),
            factory.Clock, NullLogger<DemoWorker>.Instance);
        using var http = factory.CreateClient();

        await worker.SetUpAsync(http, CancellationToken.None);
        await worker.AskAsync(http, CancellationToken.None);
        // A restart finds the same listings, and a request already waiting gets no second one.
        await worker.SetUpAsync(http, CancellationToken.None);
        await worker.AskAsync(http, CancellationToken.None);

        using var owner = await SignedInAsync(http, demo.OwnerEmail, demo.Password);
        var listings = await owner.GetFromJsonAsync<List<JsonElement>>("/api/me/listings");
        Assert.NotNull(listings);
        Assert.Equal(3, listings.Count);
        Assert.All(listings, l => Assert.False(string.IsNullOrEmpty(l.GetProperty("thumbUrl").GetString())));

        var waiting = await owner.GetFromJsonAsync<JsonElement>("/api/me/bookings?role=owner&status=Requested");
        Assert.Equal(1, waiting.GetProperty("total").GetInt32());

        using var renter = await SignedInAsync(http, demo.RenterEmail, demo.Password);
        var done = await renter.GetFromJsonAsync<JsonElement>("/api/me/bookings?role=renter&status=Completed");
        Assert.Equal(1, done.GetProperty("total").GetInt32());
    }

    /// <summary>The drawings the demo image carries, found from wherever the tests run.</summary>
    private static string PhotosFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var photos = Path.Combine(dir.FullName, "deploy", "demo", "photos");
            if (Directory.Exists(photos))
            {
                return photos;
            }
        }

        throw new DirectoryNotFoundException("deploy/demo/photos was not found above the test output.");
    }

    private async Task<HttpClient> SignedInAsync(HttpClient http, string email, string password)
    {
        var login = await http.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }

    private sealed class TestClients(ApiFactory factory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => factory.CreateClient();
    }
}
