using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Emanet.IntegrationTests.Support;

/// <summary>A signed-in person with an HTTP client that carries their token.</summary>
public sealed class Person(ApiFactory factory, SessionDto session) : IDisposable
{
    public HttpClient Http { get; } = Client(factory, session.AccessToken);

    public Guid Id => session.User.Id;

    public string Email => session.User.Email;

    public string Name => session.User.DisplayName;

    public string Token => session.AccessToken;

    public void Dispose() => Http.Dispose();

    private static HttpClient Client(ApiFactory factory, string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}

public static class TestApi
{
    public const string Password = "correct-horse-battery";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<Person> RegisterAsync(this ApiFactory factory, string name = "Ayşe Yılmaz", string? email = null)
    {
        using var anonymous = factory.CreateClient();
        email ??= $"user-{Guid.NewGuid():N}@example.com";
        var response = await anonymous.PostAsJsonAsync("/api/auth/register", new { email, displayName = name, password = Password });
        response.EnsureSuccessStatusCode();
        return new Person(factory, await response.ReadAsync<SessionDto>());
    }

    /// <summary>A listing body: a drill in Atakum unless told otherwise. Every search test adds a word of its own so results do not mix.</summary>
    public static object Body(
        string title = "Darbeli matkap, Bosch", string description = "Beton ve ahşap için. Çantası ve uç seti ile.", string category = "Tools",
        decimal price = 150m, int weekly = 20, decimal deposit = 1000m, double latitude = 41.3335, double longitude = 36.2714, string area = "Atakum, Samsun",
        int minDays = 1, int maxDays = 30, bool instantBook = false) =>
        new { title, description, category, pricePerDay = price, weeklyPercentOff = weekly, deposit, latitude, longitude, area, minDays, maxDays, instantBook };

    public static async Task<ListingDto> ListAsync(this Person owner, object? body = null)
    {
        var response = await owner.Http.PostAsJsonAsync("/api/listings", body ?? Body());
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ListingDto>();
    }

    /// <summary>Asks to rent a listing; the card works unless told otherwise.</summary>
    public static async Task<HttpResponseMessage> RequestAsync(this Person renter, Guid listingId, DateOnly start, DateOnly end, string paymentMethod = "pm_card_ok") =>
        await renter.Http.PostAsJsonAsync("/api/bookings", new { listingId, start, end, message = "Hafta sonu için lazım.", paymentMethod });

    public static async Task<BookingDto> BookAsync(this Person renter, Guid listingId, DateOnly start, DateOnly end)
    {
        var response = await renter.RequestAsync(listingId, start, end);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<BookingDto>();
    }

    public static async Task<HttpResponseMessage> PostAsync(this Person person, string path, object? body = null) =>
        await person.Http.PostAsJsonAsync(path, body ?? new { });

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
