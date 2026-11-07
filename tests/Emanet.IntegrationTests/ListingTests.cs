using System.Net;
using System.Net.Http.Json;
using Emanet.IntegrationTests.Support;

namespace Emanet.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ListingTests(ApiFactory factory)
{
    /// <summary>A spot of its own for each test, far from the others, so searches near it only see that test's listings.</summary>
    private static (double Lat, double Lng) Somewhere() => (38 + (Random.Shared.NextDouble() * 3), 28 + (Random.Shared.NextDouble() * 10));

    [Fact]
    public async Task An_owner_lists_an_item_and_sees_its_exact_place_while_others_see_a_cell()
    {
        using var owner = await factory.RegisterAsync("Mehmet Usta");
        using var stranger = await factory.RegisterAsync();
        using var anonymous = factory.CreateClient();

        var listing = await owner.ListAsync(TestApi.Body(latitude: 41.333512, longitude: 36.271438));
        var seen = await (await stranger.Http.GetAsync($"/api/listings/{listing.Id}")).ReadAsync<ListingDto>();
        var public_ = await (await anonymous.GetAsync($"/api/listings/{listing.Id}")).ReadAsync<ListingDto>();

        Assert.Equal(("Darbeli matkap, Bosch", "Tools", 150m, 20, 1000m, "Atakum, Samsun"),
            (listing.Title, listing.Category, listing.PricePerDay, listing.WeeklyPercentOff, listing.Deposit, listing.Place.Area));
        Assert.Equal((41.333512, 36.271438, true, true), (listing.Place.Latitude, listing.Place.Longitude, listing.Place.Exact, listing.IsMine));
        Assert.Equal((41.3325, 36.2725, false, false), (seen.Place.Latitude, seen.Place.Longitude, seen.Place.Exact, seen.IsMine));
        Assert.Equal(seen.Place, public_.Place);
        Assert.Equal(("Mehmet Usta", 1), (seen.Owner.Name, seen.Owner.ActiveListings));
    }

    [Fact]
    public async Task Listings_are_checked_and_only_their_owner_may_change_them()
    {
        using var owner = await factory.RegisterAsync();
        using var other = await factory.RegisterAsync();
        var listing = await owner.ListAsync();

        var cheap = await owner.Http.PostAsJsonAsync("/api/listings", TestApi.Body(price: 0.5m));
        var halfKurus = await owner.Http.PostAsJsonAsync("/api/listings", TestApi.Body(price: 10.005m));
        var backwards = await owner.Http.PostAsJsonAsync("/api/listings", TestApi.Body(minDays: 5, maxDays: 2));
        var category = await owner.Http.PostAsJsonAsync("/api/listings", TestApi.Body(category: "Boats"));
        var anonymous = await factory.CreateClient().PostAsJsonAsync("/api/listings", TestApi.Body());
        var hijack = await other.Http.PutAsJsonAsync($"/api/listings/{listing.Id}", TestApi.Body(title: "Benim artık"));
        var update = await owner.Http.PutAsJsonAsync($"/api/listings/{listing.Id}", TestApi.Body(title: "Darbeli matkap ve kırıcı", price: 175.5m));

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (cheap.StatusCode, halfKurus.StatusCode, backwards.StatusCode));
        Assert.Equal(HttpStatusCode.BadRequest, category.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal((HttpStatusCode.Forbidden, "not_your_listing"), (hijack.StatusCode, await hijack.ProblemCodeAsync()));
        var updated = await update.ReadAsync<ListingDto>();
        Assert.Equal(("Darbeli matkap ve kırıcı", 175.5m), (updated.Title, updated.PricePerDay));
    }

    [Fact]
    public async Task Search_finds_Turkish_word_forms_and_the_start_of_words()
    {
        using var owner = await factory.RegisterAsync();
        var (lat, lng) = Somewhere();
        var tent = await owner.ListAsync(TestApi.Body(title: "Dört kişilik kamp çadırı", description: "Su geçirmez, kolay kurulur.", category: "Camping", latitude: lat, longitude: lng));
        var chairs = await owner.ListAsync(TestApi.Body(title: "Katlanır sandalye takımı (4 adet)", description: "Kamp ve piknik için.", category: "Camping", latitude: lat, longitude: lng));
        var drill = await owner.ListAsync(TestApi.Body(latitude: lat, longitude: lng));
        var near = $"lat={lat}&lng={lng}&radiusKm=2";

        var tents = await Search($"q=çadır&{near}");
        var plural = await Search($"q=sandalyeler&{near}");
        var forCamping = await Search($"q=kamp&{near}");
        var partial = await Search($"q=sandaly&{near}");
        var category = await Search($"category=Tools&{near}");

        Assert.Equal([tent.Id], tents.Items.Select(i => i.Id));
        Assert.Equal([chairs.Id], plural.Items.Select(i => i.Id));
        Assert.Equal(tent.Id, forCamping.Items[0].Id);
        Assert.Equal(new[] { tent.Id, chairs.Id }.Order(), forCamping.Items.Select(i => i.Id).Order());
        Assert.Equal([chairs.Id], partial.Items.Select(i => i.Id));
        Assert.Equal([drill.Id], category.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Search_measures_distance_from_the_cell_and_sorts_by_it_or_by_price()
    {
        using var owner = await factory.RegisterAsync();
        var (lat, lng) = Somewhere();
        var here = await owner.ListAsync(TestApi.Body(price: 300m, latitude: lat + 0.001, longitude: lng));
        var further = await owner.ListAsync(TestApi.Body(price: 100m, latitude: lat + 0.05, longitude: lng));
        var outside = await owner.ListAsync(TestApi.Body(price: 50m, latitude: lat + 0.5, longitude: lng));

        var byDistance = await Search($"lat={lat}&lng={lng}&radiusKm=10&sort=Distance");
        var byPrice = await Search($"lat={lat}&lng={lng}&radiusKm=10&sort=PriceLow");
        var cheap = await Search($"lat={lat}&lng={lng}&radiusKm=60&maxPrice=120");
        using var anonymous = factory.CreateClient();
        var noPoint = await anonymous.GetAsync("/api/listings?sort=Distance");
        var half = await anonymous.GetAsync($"/api/listings?lat={lat}");

        Assert.Equal([here.Id, further.Id], byDistance.Items.Select(i => i.Id));
        Assert.Equal([further.Id, here.Id], byPrice.Items.Select(i => i.Id));
        Assert.Equal(new[] { further.Id, outside.Id }.Order(), cheap.Items.Select(i => i.Id).Order());
        Assert.InRange(byDistance.Items[1].DistanceKm!.Value, 5, 6.2);
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (noPoint.StatusCode, half.StatusCode));
        // A result never gives away more than its cell.
        Assert.All(byDistance.Items, i => Assert.Equal(25d, Math.Round(i.Latitude * 10000) % 50));
    }

    [Fact]
    public async Task Paused_and_removed_listings_leave_search_and_the_owner_still_sees_paused_ones()
    {
        using var owner = await factory.RegisterAsync();
        var (lat, lng) = Somewhere();
        var paused = await owner.ListAsync(TestApi.Body(latitude: lat, longitude: lng));
        var removed = await owner.ListAsync(TestApi.Body(latitude: lat, longitude: lng));
        var shown = await owner.ListAsync(TestApi.Body(latitude: lat, longitude: lng));

        (await owner.PostAsync($"/api/listings/{paused.Id}/pause")).EnsureSuccessStatusCode();
        (await owner.Http.DeleteAsync($"/api/listings/{removed.Id}")).EnsureSuccessStatusCode();
        var found = await Search($"lat={lat}&lng={lng}&radiusKm=1");
        var mine = await (await owner.Http.GetAsync("/api/me/listings")).ReadAsync<List<ListingSummaryDto>>();
        using var anonymous = factory.CreateClient();
        var gone = await anonymous.GetAsync($"/api/listings/{removed.Id}");
        var resumed = await (await owner.PostAsync($"/api/listings/{paused.Id}/resume")).ReadAsync<ListingDto>();
        var editRemoved = await owner.Http.PutAsJsonAsync($"/api/listings/{removed.Id}", TestApi.Body());

        Assert.Equal([shown.Id], found.Items.Select(i => i.Id));
        Assert.Equal(new[] { shown.Id, paused.Id }.Order(), mine.Select(m => m.Id).Order());
        Assert.Equal("Paused", mine.Single(m => m.Id == paused.Id).Status);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("Active", resumed.Status);
        Assert.Equal(HttpStatusCode.NotFound, editRemoved.StatusCode);
    }

    [Fact]
    public async Task Categories_count_active_listings()
    {
        using var owner = await factory.RegisterAsync();
        using var anonymous = factory.CreateClient();
        var before = await (await anonymous.GetAsync("/api/listings/categories")).ReadAsync<List<CategoryDto>>();
        await owner.ListAsync(TestApi.Body(category: "Music", title: "Akustik gitar, Yamaha"));

        var after = await (await anonymous.GetAsync("/api/listings/categories")).ReadAsync<List<CategoryDto>>();

        Assert.Equal(11, after.Count);
        Assert.Equal(before.Single(c => c.Key == "Music").ActiveListings + 1, after.Single(c => c.Key == "Music").ActiveListings);
    }

    [Fact]
    public async Task Results_come_in_pages()
    {
        using var owner = await factory.RegisterAsync();
        var (lat, lng) = Somewhere();
        for (var i = 0; i < 5; i++)
        {
            await owner.ListAsync(TestApi.Body(title: $"Kamp sandalyesi {i + 1}", latitude: lat, longitude: lng));
            factory.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        var first = await Search($"lat={lat}&lng={lng}&radiusKm=1&pageSize=2&sort=Newest");
        var last = await Search($"lat={lat}&lng={lng}&radiusKm=1&pageSize=2&page=3&sort=Newest");

        Assert.Equal((5, 2, "Kamp sandalyesi 5"), (first.Total, first.Items.Count, first.Items[0].Title));
        Assert.Equal(["Kamp sandalyesi 1"], last.Items.Select(i => i.Title));
    }

    private async Task<PageDto<ListingSummaryDto>> Search(string query)
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync($"/api/listings?{query}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<PageDto<ListingSummaryDto>>();
    }
}
