using System.Net;
using System.Net.Http.Json;
using Emanet.IntegrationTests.Support;

namespace Emanet.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class BookingTests(ApiFactory factory)
{
    [Fact]
    public async Task A_request_holds_the_money_and_accepting_it_charges_the_rent_into_the_books()
    {
        using var owner = await factory.RegisterAsync("Mehmet Usta");
        using var renter = await factory.RegisterAsync("Ayşe Kaya");
        var listing = await owner.ListAsync();

        var requested = await renter.BookAsync(listing.Id, factory.Day(3), factory.Day(10));
        var accepted = await (await owner.PostAsync($"/api/bookings/{requested.Id}/accept")).ReadAsync<BookingDto>();
        var renterView = await (await renter.Http.GetAsync($"/api/bookings/{requested.Id}")).ReadAsync<BookingDto>();
        var earnings = await (await owner.Http.GetAsync("/api/me/earnings")).ReadAsync<EarningsDto>();
        var listingForRenter = await (await renter.Http.GetAsync($"/api/listings/{listing.Id}")).ReadAsync<ListingDto>();

        Assert.Equal(("Requested", 7, 840m, 1000m, false), (requested.Status, requested.Days, requested.Rent, requested.Deposit, requested.Payment.RentCharged));
        Assert.Equal(("Confirmed", true, false), (accepted.Status, accepted.Payment.RentCharged, accepted.Payment.HoldsReleased));
        Assert.Equal((84m, 756m), (accepted.Fee, accepted.OwnerShare));
        Assert.Equal((null, null, true), (renterView.Fee, renterView.OwnerShare, renterView.Place.Exact));
        Assert.True(listingForRenter.Place.Exact);
        Assert.Equal((756m, 756m, 0m), (earnings.Owed, earnings.Earned, earnings.Refunded));
        Assert.Equal(["Requested", "Confirmed", "RentCharged"], accepted.History.Select(h => h.Kind));
        Assert.Equal(2L, await factory.ScalarAsync<long>("SELECT count(*) FROM fakepay.calls c JOIN fakepay.holds h ON h.id = c.hold_id WHERE c.key LIKE @p0", $"booking:{requested.Id}:hold-%"));
    }

    [Fact]
    public async Task Prices_are_quoted_and_taken_days_show_on_the_calendar_without_saying_who()
    {
        using var owner = await factory.RegisterAsync();
        using var renter = await factory.RegisterAsync();
        using var anonymous = factory.CreateClient();
        var listing = await owner.ListAsync(TestApi.Body(instantBook: true));
        await renter.BookAsync(listing.Id, factory.Day(5), factory.Day(8));
        (await owner.Http.PostAsJsonAsync($"/api/listings/{listing.Id}/blocks", new { start = factory.Day(20), end = factory.Day(22), note = "Tatildeyim" })).EnsureSuccessStatusCode();

        var week = await (await anonymous.GetAsync($"/api/listings/{listing.Id}/quote?start={factory.Day(30):yyyy-MM-dd}&end={factory.Day(37):yyyy-MM-dd}")).ReadAsync<QuoteDto>();
        var clash = await (await anonymous.GetAsync($"/api/listings/{listing.Id}/quote?start={factory.Day(7):yyyy-MM-dd}&end={factory.Day(9):yyyy-MM-dd}")).ReadAsync<QuoteDto>();
        var publicCalendar = await (await anonymous.GetAsync($"/api/listings/{listing.Id}/calendar")).ReadAsync<List<CalendarRangeDto>>();
        var ownerCalendar = await (await owner.Http.GetAsync($"/api/listings/{listing.Id}/calendar")).ReadAsync<List<CalendarRangeDto>>();

        Assert.Equal((7, 1050m, 210m, 840m, 1000m, true), (week.Days, week.FullPrice, week.Discount, week.Rent, week.Deposit, week.Available));
        Assert.Equal((false, "Some of these days are taken."), (clash.Available, clash.Reason));
        Assert.Equal(["Taken", "Taken"], publicCalendar.Select(c => c.Kind));
        Assert.All(publicCalendar, c => Assert.Null(c.Id));
        Assert.Equal([("Booked", (string?)null), ("Closed", "Tatildeyim")], ownerCalendar.Select(c => (c.Kind, c.Note)));
    }

    [Fact]
    public async Task Instant_booking_confirms_at_once_and_the_same_days_cannot_be_had_twice()
    {
        using var owner = await factory.RegisterAsync();
        using var first = await factory.RegisterAsync();
        using var second = await factory.RegisterAsync();
        var listing = await owner.ListAsync(TestApi.Body(instantBook: true));

        var booked = await first.BookAsync(listing.Id, factory.Day(10), factory.Day(13));
        var overlapping = await second.RequestAsync(listing.Id, factory.Day(12), factory.Day(15));
        var backToBack = await second.BookAsync(listing.Id, factory.Day(13), factory.Day(15));
        var closing = await owner.Http.PostAsJsonAsync($"/api/listings/{listing.Id}/blocks", new { start = factory.Day(14), end = factory.Day(16) });

        Assert.Equal(("Confirmed", true), (booked.Status, booked.Payment.RentCharged));
        Assert.Equal((HttpStatusCode.Conflict, "unavailable"), (overlapping.StatusCode, await overlapping.ProblemCodeAsync()));
        Assert.Equal("Confirmed", backToBack.Status);
        Assert.Equal(HttpStatusCode.Conflict, closing.StatusCode);
    }

    [Fact]
    public async Task Twenty_people_booking_the_same_days_at_once_leaves_exactly_one_rental_and_nobody_charged_for_nothing()
    {
        using var owner = await factory.RegisterAsync();
        var listing = await owner.ListAsync(TestApi.Body(instantBook: true));
        var renters = new List<Person>();
        for (var i = 0; i < 20; i++)
        {
            renters.Add(await factory.RegisterAsync($"Kiracı {i}"));
        }

        var start = factory.Day(40);
        var responses = await Task.WhenAll(renters.Select((r, n) => r.RequestAsync(listing.Id, start.AddDays(n % 3), start.AddDays(4))));
        await factory.SweepAsync();

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1L, await factory.ScalarAsync<long>("SELECT count(*) FROM calendar WHERE listing_id = @p0", listing.Id));
        Assert.Equal(1L, await factory.ScalarAsync<long>("SELECT count(*) FROM bookings WHERE listing_id = @p0 AND status = 'Confirmed'", listing.Id));
        // Everyone who lost had their holds let go, before or after their request was saved.
        Assert.Equal(0L, await factory.ScalarAsync<long>(
            "SELECT count(*) FROM fakepay.holds h JOIN fakepay.calls c ON c.hold_id = h.id AND c.kind = 'hold' " +
            "WHERE h.status = 'Held' AND c.key LIKE 'booking:%' AND split_part(c.key, ':', 2)::uuid IN (SELECT id FROM bookings WHERE listing_id = @p0 AND status <> 'Confirmed')", listing.Id));
        Assert.Equal(1L, await factory.ScalarAsync<long>("SELECT count(*) FROM journal WHERE booking_id IN (SELECT id FROM bookings WHERE listing_id = @p0)", listing.Id));
        renters.ForEach(r => r.Dispose());
    }

    [Fact]
    public async Task Accepting_one_request_declines_the_others_for_those_days_and_lets_their_holds_go()
    {
        using var owner = await factory.RegisterAsync();
        using var a = await factory.RegisterAsync();
        using var b = await factory.RegisterAsync();
        using var c = await factory.RegisterAsync();
        var listing = await owner.ListAsync();
        var wanted = await a.BookAsync(listing.Id, factory.Day(5), factory.Day(8));
        var rival = await b.BookAsync(listing.Id, factory.Day(7), factory.Day(9));
        var later = await c.BookAsync(listing.Id, factory.Day(8), factory.Day(10));

        (await owner.PostAsync($"/api/bookings/{wanted.Id}/accept")).EnsureSuccessStatusCode();
        var lost = await (await b.Http.GetAsync($"/api/bookings/{rival.Id}")).ReadAsync<BookingDto>();
        var untouched = await (await c.Http.GetAsync($"/api/bookings/{later.Id}")).ReadAsync<BookingDto>();
        var stranger = await c.Http.GetAsync($"/api/bookings/{wanted.Id}");
        var renterAccepts = await c.PostAsync($"/api/bookings/{later.Id}/accept");

        Assert.Equal(("Declined", "Those days went to another renter.", true), (lost.Status, lost.Reason, lost.Payment.HoldsReleased));
        Assert.Equal(("Requested", true), (untouched.Status, untouched.CanCancel));
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, renterAccepts.StatusCode);
    }

    [Fact]
    public async Task A_declined_or_unanswered_request_releases_the_holds()
    {
        using var owner = await factory.RegisterAsync();
        using var renter = await factory.RegisterAsync();
        var listing = await owner.ListAsync();
        var declined = await renter.BookAsync(listing.Id, factory.Day(3), factory.Day(4));
        var ignored = await renter.BookAsync(listing.Id, factory.Day(6), factory.Day(7));

        var refused = await (await owner.PostAsync($"/api/bookings/{declined.Id}/decline", new { reason = "O hafta kullanacağım." })).ReadAsync<BookingDto>();
        factory.Clock.Advance(TimeSpan.FromHours(25));
        var pass = await factory.SweepAsync();

        Assert.Equal(("Declined", "O hafta kullanacağım.", true), (refused.Status, refused.Reason, refused.Payment.HoldsReleased));
        Assert.True(pass.Expired >= 1);
        Assert.Equal("Expired", await factory.ScalarAsync<string>("SELECT status FROM bookings WHERE id = @p0", ignored.Id));
        Assert.Equal(2L, await factory.ScalarAsync<long>(
            "SELECT count(*) FROM fakepay.holds h JOIN fakepay.calls c ON c.hold_id = h.id AND c.kind = 'hold' WHERE c.key LIKE @p0 AND h.status = 'Released'", $"booking:{ignored.Id}:%"));
    }

    [Fact]
    public async Task Cancelling_refunds_by_the_policy_and_the_books_follow()
    {
        using var owner = await factory.RegisterAsync();
        using var renter = await factory.RegisterAsync();
        var listing = await owner.ListAsync(TestApi.Body(instantBook: true, price: 100m, weekly: 0, deposit: 0m));
        var early = await renter.BookAsync(listing.Id, factory.Day(10), factory.Day(12));
        var late = await renter.BookAsync(listing.Id, factory.Day(1), factory.Day(3));
        var byOwner = await renter.BookAsync(listing.Id, factory.Day(20), factory.Day(22));

        var full = await (await renter.PostAsync($"/api/bookings/{early.Id}/cancel", new { reason = "Planlar değişti" })).ReadAsync<BookingDto>();
        var half = await (await renter.PostAsync($"/api/bookings/{late.Id}/cancel")).ReadAsync<BookingDto>();
        var ownerRefund = await (await owner.PostAsync($"/api/bookings/{byOwner.Id}/cancel", new { reason = "Matkap bozuldu" })).ReadAsync<BookingDto>();
        var earnings = await (await owner.Http.GetAsync("/api/me/earnings")).ReadAsync<EarningsDto>();
        var again = await renter.BookAsync(listing.Id, factory.Day(10), factory.Day(12));

        Assert.Equal(("Cancelled", 200m, true), (full.Status, full.Payment.Refund, full.Payment.Refunded));
        Assert.Equal((100m, true), (half.Payment.Refund, half.Payment.Refunded));
        Assert.Equal(200m, ownerRefund.Payment.Refund);
        // Three rentals of 200 TL, 180 each for the owner; refunds of 200, 100 and 200 take back 180, 90 and 180.
        Assert.Equal((90m, 540m, 450m), (earnings.Owed, earnings.Earned, earnings.Refunded));
        Assert.Equal("Confirmed", again.Status);
    }

    [Fact]
    public async Task A_refused_card_holds_nothing_and_saves_no_request()
    {
        using var owner = await factory.RegisterAsync();
        using var renter = await factory.RegisterAsync();
        var listing = await owner.ListAsync();

        var declined = await renter.RequestAsync(listing.Id, factory.Day(3), factory.Day(5), "pm_card_declined");
        var poor = await renter.RequestAsync(listing.Id, factory.Day(3), factory.Day(5), "pm_card_no_funds");
        var mine = await (await renter.Http.GetAsync("/api/me/bookings?role=renter")).ReadAsync<PageDto<BookingSummaryDto>>();

        Assert.Equal((HttpStatusCode.PaymentRequired, "payment_declined"), (declined.StatusCode, await declined.ProblemCodeAsync()));
        Assert.Equal(HttpStatusCode.PaymentRequired, poor.StatusCode);
        Assert.Empty(mine.Items);
    }

    [Fact]
    public async Task Requests_are_checked()
    {
        using var owner = await factory.RegisterAsync();
        using var renter = await factory.RegisterAsync();
        var listing = await owner.ListAsync(TestApi.Body(minDays: 2, maxDays: 5));

        var past = await renter.RequestAsync(listing.Id, factory.Day(-1), factory.Day(2));
        var tooShort = await renter.RequestAsync(listing.Id, factory.Day(1), factory.Day(2));
        var tooLong = await renter.RequestAsync(listing.Id, factory.Day(1), factory.Day(7));
        var own = await owner.RequestAsync(listing.Id, factory.Day(1), factory.Day(3));
        (await owner.PostAsync($"/api/listings/{listing.Id}/pause")).EnsureSuccessStatusCode();
        var paused = await renter.RequestAsync(listing.Id, factory.Day(1), factory.Day(3));

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (past.StatusCode, tooShort.StatusCode, tooLong.StatusCode));
        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (own.StatusCode, paused.StatusCode));
    }

    [Fact]
    public async Task When_the_provider_is_down_the_rental_is_confirmed_and_the_sweeper_charges_it_later_once()
    {
        using var owner = await factory.RegisterAsync();
        using var renter = await factory.RegisterAsync();
        var listing = await owner.ListAsync(TestApi.Body(instantBook: true));

        factory.Payments.CapturesFail = true;
        BookingDto booked;
        try
        {
            booked = await renter.BookAsync(listing.Id, factory.Day(50), factory.Day(52));
            await factory.SweepAsync();
        }
        finally
        {
            factory.Payments.CapturesFail = false;
        }

        await factory.SweepAsync();
        await factory.SweepAsync();
        var settled = await (await renter.Http.GetAsync($"/api/bookings/{booked.Id}")).ReadAsync<BookingDto>();

        Assert.Equal(("Confirmed", false), (booked.Status, booked.Payment.RentCharged));
        Assert.True(settled.Payment.RentCharged);
        Assert.Equal(1L, await factory.ScalarAsync<long>("SELECT count(*) FROM journal WHERE booking_id = @p0", booked.Id));
    }

    [Fact]
    public async Task Search_by_dates_shows_only_what_is_free_for_all_of_them()
    {
        using var owner = await factory.RegisterAsync();
        using var renter = await factory.RegisterAsync();
        var (lat, lng) = (37.5 + (Random.Shared.NextDouble() * 0.4), 27.2 + (Random.Shared.NextDouble() * 0.4));
        var busy = await owner.ListAsync(TestApi.Body(instantBook: true, latitude: lat, longitude: lng));
        var free = await owner.ListAsync(TestApi.Body(latitude: lat, longitude: lng));
        var weekOnly = await owner.ListAsync(TestApi.Body(minDays: 7, latitude: lat, longitude: lng));
        await renter.BookAsync(busy.Id, factory.Day(30), factory.Day(33));
        using var anonymous = factory.CreateClient();

        var found = await (await anonymous.GetAsync($"/api/listings?lat={lat}&lng={lng}&radiusKm=1&from={factory.Day(32):yyyy-MM-dd}&to={factory.Day(35):yyyy-MM-dd}"))
            .ReadAsync<PageDto<ListingSummaryDto>>();

        Assert.Equal([free.Id], found.Items.Select(i => i.Id));
        Assert.DoesNotContain(weekOnly.Id, found.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task The_books_balance_and_match_the_payment_provider()
    {
        using var admin = await factory.RegisterAsync("Muhasebe", ApiFactory.AdminEmail);
        using var someone = await factory.RegisterAsync();

        var books = await (await admin.Http.GetAsync("/api/admin/books")).ReadAsync<BooksDto>();
        var refused = await someone.Http.GetAsync("/api/admin/books");

        Assert.True(books.Balanced);
        Assert.True(books.Reconciled, $"ledger {books.ProviderClearing} vs provider {books.ProviderSays}");
        Assert.Equal(0m, books.LedgerTotal);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task The_database_refuses_an_unbalanced_entry_and_any_change_to_the_ledger()
    {
        var entry = Guid.NewGuid();
        var unbalanced = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => factory.ScalarAsync<object>(
            "BEGIN; INSERT INTO journal (id, kind, key, memo, posted_at) VALUES (@p0, 'Payout', @p1, 'test', now()); " +
            "INSERT INTO ledger_lines (entry_id, account, amount) VALUES (@p0, 'bank:payouts', 100), (@p0, 'platform:fees', -99); COMMIT;", entry, $"test:{entry}"));
        var balanced = Guid.NewGuid();
        await factory.ScalarAsync<object>(
            "INSERT INTO journal (id, kind, key, memo, posted_at) VALUES (@p0, 'Payout', @p1, 'test', now()); " +
            "INSERT INTO ledger_lines (entry_id, account, amount) VALUES (@p0, 'bank:payouts', 100), (@p0, 'platform:fees', -100);", balanced, $"test:{balanced}");
        var edit = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => factory.ScalarAsync<object>("UPDATE ledger_lines SET amount = 99 WHERE entry_id = @p0", balanced));
        var removal = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => factory.ScalarAsync<object>("DELETE FROM journal WHERE id = @p0", balanced));

        Assert.Contains("does not balance", unbalanced.MessageText);
        Assert.Contains("ledger_lines is append-only", edit.MessageText);
        Assert.Contains("journal is append-only", removal.MessageText);
    }
}
