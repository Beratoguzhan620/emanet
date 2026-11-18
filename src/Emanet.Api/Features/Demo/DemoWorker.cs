using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Emanet.Api.Features.Demo;

/// <summary>
/// Fills the public demo through the API's own endpoints, so everything in it passes the same rules as what a visitor
/// does. On start it makes the neighbourhood once: three owners around Samsun with eight things for rent and their
/// photos, a few finished rentals with reviews on both sides, and the published renter, owner and support sign-ins.
/// Then, while the service is awake, the published owner always has a request from a neighbour waiting for an answer.
/// </summary>
internal sealed partial class DemoWorker(
    IOptions<DemoOptions> options, IHttpClientFactory clients, IHostApplicationLifetime lifetime, TimeProvider clock, ILogger<DemoWorker> logger)
    : BackgroundService
{
    public const string HttpClientName = "Emanet.Demo";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static readonly (string Key, string Owner, string Kind, string Title, string Category, decimal Price, int Weekly, decimal Deposit, double Lat, double Lng, string Area, bool Instant, string Description)[] Items =
    [
        ("drill", "owner", "drill", "Darbeli matkap, Bosch 750 W", "Tools", 150, 20, 1000, 41.3341, 36.2712, "Atakum, Samsun", false, "Beton ve ahşap için. Çantası, 12 parça uç seti ve yedek kömürleri ile."),
        ("ladder", "owner", "ladder", "Alüminyum merdiven, 7 basamak", "Tools", 90, 15, 500, 41.3318, 36.2766, "Atakum, Samsun", true, "Boya, perde, ampul işleri için. Katlanınca bagaja sığar."),
        ("tent", "elif", "tent", "Kamp çadırı, 4 kişilik", "Camping", 220, 25, 1500, 41.2894, 36.3297, "İlkadım, Samsun", true, "Çift katlı, su geçirmez. Kazıklar ve taşıma çantası dahil."),
        ("chairs", "elif", "chairs", "Katlanır kamp sandalyesi, 2 adet", "Camping", 60, 10, 300, 41.2911, 36.3342, "İlkadım, Samsun", true, "Hafif, bardaklıklı. Plaj ve piknik için."),
        ("camera", "burak", "camera", "Aynasız fotoğraf makinesi ve 35 mm lens", "Photo", 450, 30, 6000, 41.2768, 36.3439, "Canik, Samsun", false, "Düğün ve gezi çekimleri için. İki batarya, 64 GB kart, şarj aleti."),
        ("projector", "burak", "projector", "Projeksiyon cihazı, Full HD", "Electronics", 280, 20, 3000, 41.2803, 36.3398, "Canik, Samsun", false, "Maç akşamları ve sunumlar için. HDMI kablosu ve perde dahil."),
        ("bike", "elif", "bike", "Şehir bisikleti, 28 jant", "Sports", 120, 25, 1200, 41.3165, 36.2987, "Atakum, Samsun", true, "Sahil yolu için ideal. Kask ve kilit dahil."),
        ("speaker", "owner", "speaker", "Taşınabilir hoparlör, 40 W", "Music", 110, 10, 800, 41.3355, 36.2689, "Atakum, Samsun", true, "Bahçe partileri için. 12 saat pil."),
    ];

    private readonly Dictionary<string, Person> _people = [];
    private readonly Dictionary<string, Guid> _listings = [];
    private DateTimeOffset _signedInAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await StartedAsync(stoppingToken))
        {
            return;
        }

        using var http = clients.CreateClient(HttpClientName);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_people.Count == 0 || clock.GetUtcNow() - _signedInAt > TimeSpan.FromHours(6))
                {
                    await SetUpAsync(http, stoppingToken);
                }

                await AskAsync(http, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // The demo keeps going; the next round tries again.
                LogFailed(logger, ex);
            }

            await Task.Delay(options.Value.Every, clock, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> StartedAsync(CancellationToken ct)
    {
        var started = new TaskCompletionSource();
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await Task.WhenAny(started.Task, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
        return started.Task.IsCompleted && !ct.IsCancellationRequested;
    }

    /// <summary>Signs everyone in, making the neighbourhood the first time.</summary>
    public async Task SetUpAsync(HttpClient http, CancellationToken ct)
    {
        var demo = options.Value;
        _people["renter"] = await SignInAsync(http, demo.RenterEmail, "Ayşe Kaya", ct);
        _people["owner"] = await SignInAsync(http, demo.OwnerEmail, "Mehmet Usta", ct);
        _people["support"] = await SignInAsync(http, demo.SupportEmail, "Destek", ct);
        _people["elif"] = await SignInAsync(http, "elif@neighbours.emanet.demo", "Elif Şahin", ct);
        _people["burak"] = await SignInAsync(http, "burak@neighbours.emanet.demo", "Burak Yıldız", ct);
        _people["can"] = await SignInAsync(http, "can@neighbours.emanet.demo", "Can Demir", ct);

        foreach (var owner in Items.Select(i => i.Owner).Distinct())
        {
            foreach (var listing in await SendAsync<List<Listing>>(http, HttpMethod.Get, "/api/me/listings", _people[owner].Token, null, ct))
            {
                var key = Items.FirstOrDefault(i => i.Title == listing.Title).Key;
                if (key is not null)
                {
                    _listings[key] = listing.Id;
                }
            }
        }

        if (_listings.Count == 0)
        {
            await CreateNeighbourhoodAsync(http, ct);
        }

        _signedInAt = clock.GetUtcNow();
        LogReady(logger, _listings.Count);
    }

    private async Task CreateNeighbourhoodAsync(HttpClient http, CancellationToken ct)
    {
        foreach (var item in Items)
        {
            var owner = _people[item.Owner];
            var listing = await SendAsync<Listing>(http, HttpMethod.Post, "/api/listings", owner.Token, new
            {
                title = item.Title,
                description = item.Description,
                category = item.Category,
                pricePerDay = item.Price,
                weeklyPercentOff = item.Weekly,
                deposit = item.Deposit,
                latitude = item.Lat,
                longitude = item.Lng,
                area = item.Area,
                minDays = 1,
                maxDays = 14,
                instantBook = item.Instant,
            }, ct);
            _listings[item.Key] = listing.Id;
            await UploadAsync(http, $"/api/listings/{listing.Id}/photos", owner, item.Kind, ct);
        }

        // Finished rentals with reviews on both sides, so the listings have stars.
        foreach (var (key, renter, stars, comment) in new[]
                 {
                     ("tent", "can", 5, "Çadır tertemiz ve eksiksizdi. Elif Hanım kurulumu da anlattı."),
                     ("ladder", "renter", 5, "Tam zamanında, çok sağlam."),
                     ("chairs", "can", 4, "Rahat sandalyeler, biri biraz gıcırdıyor."),
                 })
        {
            var owner = _people[Items.First(i => i.Key == key).Owner];
            var who = _people[renter];
            var booking = await BookAsync(http, who, key, 0, 1, "", ct);
            if (booking.Status == "Requested")
            {
                await SendAsync<JsonElement?>(http, HttpMethod.Post, $"/api/bookings/{booking.Id}/accept", owner.Token, new { }, ct);
            }

            var pickup = (await SendAsync<Booking>(http, HttpMethod.Get, $"/api/bookings/{booking.Id}", who.Token, null, ct)).Handover.PickupCode;
            await SendAsync<JsonElement?>(http, HttpMethod.Post, $"/api/bookings/{booking.Id}/pickup", owner.Token, new { code = pickup }, ct);
            var giveBack = (await SendAsync<Booking>(http, HttpMethod.Get, $"/api/bookings/{booking.Id}", owner.Token, null, ct)).Handover.ReturnCode;
            await SendAsync<JsonElement?>(http, HttpMethod.Post, $"/api/bookings/{booking.Id}/return", who.Token, new { code = giveBack }, ct);
            await SendAsync<JsonElement?>(http, HttpMethod.Post, $"/api/bookings/{booking.Id}/all-good", owner.Token, new { }, ct);
            await SendAsync<JsonElement?>(http, HttpMethod.Post, $"/api/bookings/{booking.Id}/review", who.Token, new { stars, comment }, ct);
            await SendAsync<JsonElement?>(http, HttpMethod.Post, $"/api/bookings/{booking.Id}/review", owner.Token,
                new { stars = 5, comment = "Özenle kullanmış, zamanında getirdi." }, ct);
        }
    }

    /// <summary>A neighbour asks the published owner for the drill, when no request of theirs is waiting.</summary>
    public async Task AskAsync(HttpClient http, CancellationToken ct)
    {
        var owner = _people["owner"];
        var waiting = await SendAsync<BookingPage>(http, HttpMethod.Get, "/api/me/bookings?role=owner&status=Requested", owner.Token, null, ct);
        if (waiting.Total > 0)
        {
            return;
        }

        // A few days ahead, moving on when those days are already taken.
        for (var from = 3; from < 30; from += 3)
        {
            var quote = await SendAsync<Quote>(http, HttpMethod.Get,
                $"/api/listings/{_listings["drill"]}/quote?start={Day(from):yyyy-MM-dd}&end={Day(from + 1):yyyy-MM-dd}", owner.Token, null, ct);
            if (quote.Available)
            {
                await BookAsync(http, _people["can"], "drill", from, from + 1, "Merhaba, balkona raf takacağım. Hafta sonu alabilir miyim?", ct);
                return;
            }
        }
    }

    private Task<Booking> BookAsync(HttpClient http, Person who, string key, int from, int to, string message, CancellationToken ct) =>
        SendAsync<Booking>(http, HttpMethod.Post, "/api/bookings", who.Token, new
        {
            listingId = _listings[key],
            start = Day(from).ToString("yyyy-MM-dd"),
            end = Day(to).ToString("yyyy-MM-dd"),
            message,
            paymentMethod = "pm_card_ok",
        }, ct);

    private DateOnly Day(int plus) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime).AddDays(plus);

    private async Task UploadAsync(HttpClient http, string path, Person owner, string kind, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(options.Value.PhotosPath, $"{kind}.jpg"), ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"POST {path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        }
    }

    private async Task<Person> SignInAsync(HttpClient http, string email, string name, CancellationToken ct)
    {
        var password = options.Value.Password;
        using var login = await http.PostAsJsonAsync("/api/auth/login", new { email, password }, ct);
        using var register = login.IsSuccessStatusCode
            ? null
            : await http.PostAsJsonAsync("/api/auth/register", new { email, displayName = name, password }, ct);
        var response = register ?? login;
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The demo account {email} could not sign in: {(int)response.StatusCode}.");
        }

        var session = (await response.Content.ReadFromJsonAsync<Session>(ct))!;
        return new Person(session.User.Id, session.AccessToken);
    }

    private static async Task<T> SendAsync<T>(HttpClient http, HttpMethod method, string path, string token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{method} {path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        }

        return response.StatusCode == System.Net.HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0
            ? default!
            : (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    private sealed record Person(Guid Id, string Token);

    private sealed record Session(string AccessToken, SessionUser User);

    private sealed record SessionUser(Guid Id);

    private sealed record Listing(Guid Id, string Title);

    private sealed record Handover(string? PickupCode, string? ReturnCode);

    private sealed record Booking(Guid Id, string Status, Handover Handover);

    private sealed record BookingPage(int Total);

    private sealed record Quote(bool Available);

    [LoggerMessage(Level = LogLevel.Information, Message = "The demo neighbourhood has {Listings} listings")]
    private static partial void LogReady(ILogger log, int listings);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A demo round failed")]
    private static partial void LogFailed(ILogger log, Exception exception);
}
