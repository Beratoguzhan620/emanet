using System.Net;
using System.Net.Http.Headers;
using Emanet.IntegrationTests.Support;
using Emanet.UnitTests;

namespace Emanet.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class HandoverTests(ApiFactory factory)
{
    [Fact]
    public async Task Each_side_types_in_the_code_from_the_others_screen_and_a_clean_return_lets_the_deposit_go()
    {
        var (owner, renter, booking) = await RentedAsync();
        using var _ = owner;
        using var __ = renter;
        var ownerView = await GetAsync(owner, booking.Id);
        var renterView = await GetAsync(renter, booking.Id);

        var byRenter = await renter.PostAsync($"/api/bookings/{booking.Id}/pickup", new { code = renterView.Handover.PickupCode });
        var wrong = await owner.PostAsync($"/api/bookings/{booking.Id}/pickup", new { code = Other(renterView.Handover.PickupCode!) });
        var picked = await (await owner.PostAsync($"/api/bookings/{booking.Id}/pickup", new { code = renterView.Handover.PickupCode })).ReadAsync<BookingDto>();
        var returnCode = (await GetAsync(owner, booking.Id)).Handover.ReturnCode;
        var renterWhileOut = await GetAsync(renter, booking.Id);
        var back = await (await renter.PostAsync($"/api/bookings/{booking.Id}/return", new { code = returnCode })).ReadAsync<BookingDto>();
        var ownerAfter = await GetAsync(owner, booking.Id);
        var done = await (await owner.PostAsync($"/api/bookings/{booking.Id}/all-good")).ReadAsync<BookingDto>();

        Assert.Equal((null, null, true), (ownerView.Handover.PickupCode, ownerView.Handover.ReturnCode, ownerView.Handover.CanPickUp));
        Assert.Matches("^[0-9]{6}$", renterView.Handover.PickupCode);
        Assert.False(renterView.Handover.CanPickUp);
        Assert.Equal(HttpStatusCode.Forbidden, byRenter.StatusCode);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "wrong_code"), (wrong.StatusCode, await wrong.ProblemCodeAsync()));
        Assert.Equal("Active", picked.Status);
        Assert.Matches("^[0-9]{6}$", returnCode);
        Assert.Equal((null, true), (renterWhileOut.Handover.ReturnCode, renterWhileOut.Handover.CanGiveBack));
        Assert.Equal(("Returned", false, false), (back.Status, back.Handover.CanReportDamage, back.Handover.CanSignOff));
        Assert.Equal((true, true), (ownerAfter.Handover.CanReportDamage, ownerAfter.Handover.CanSignOff));
        Assert.Equal(("Completed", true, 0m), (done.Status, done.Payment.DepositSettled, done.Payment.DepositCharged));
        Assert.Contains("DepositReleased", done.History.Select(h => h.Kind));
        Assert.Equal("Released", await HoldStatusAsync(booking.Id, "deposit"));
    }

    [Fact]
    public async Task Five_wrong_codes_put_a_new_one_on_the_renters_screen()
    {
        var (owner, renter, booking) = await RentedAsync();
        using var _ = owner;
        using var __ = renter;
        var code = (await GetAsync(renter, booking.Id)).Handover.PickupCode!;

        var answers = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            answers.Add(await owner.PostAsync($"/api/bookings/{booking.Id}/pickup", new { code = Other(code) }));
        }

        var renewed = (await GetAsync(renter, booking.Id)).Handover.PickupCode!;
        var oldCode = await owner.PostAsync($"/api/bookings/{booking.Id}/pickup", new { code });
        var newCode = await owner.PostAsync($"/api/bookings/{booking.Id}/pickup", new { code = renewed });

        Assert.All(answers, a => Assert.Equal(HttpStatusCode.UnprocessableEntity, a.StatusCode));
        Assert.NotEqual(code, renewed);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, oldCode.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newCode.StatusCode);
    }

    [Fact]
    public async Task Nothing_is_picked_up_before_the_first_day()
    {
        var (owner, renter, booking) = await RentedAsync(fromDay: 2);
        using var _ = owner;
        using var __ = renter;
        var code = (await GetAsync(renter, booking.Id)).Handover.PickupCode;

        var early = await owner.PostAsync($"/api/bookings/{booking.Id}/pickup", new { code });

        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.False((await GetAsync(owner, booking.Id)).Handover.CanPickUp);
    }

    [Fact]
    public async Task An_accepted_damage_claim_takes_that_much_of_the_deposit_and_lets_the_rest_go()
    {
        var (owner, renter, booking) = await ReturnedAsync();
        using var _ = owner;
        using var __ = renter;

        var tooMuch = await owner.PostAsync($"/api/bookings/{booking.Id}/claim", new { amount = 1000.01m, note = "Uç kırık, çanta yırtık geldi." });
        var vague = await owner.PostAsync($"/api/bookings/{booking.Id}/claim", new { amount = 300m, note = "Kırık" });
        var byRenter = await renter.PostAsync($"/api/bookings/{booking.Id}/claim", new { amount = 300m, note = "Uç kırık, çanta yırtık geldi." });
        var claimed = await (await owner.PostAsync($"/api/bookings/{booking.Id}/claim", new { amount = 300m, note = "Uç kırık, çanta yırtık geldi." })).ReadAsync<BookingDto>();
        var renterSees = await GetAsync(renter, booking.Id);
        var accepted = await (await renter.PostAsync($"/api/bookings/{booking.Id}/claim/accept")).ReadAsync<BookingDto>();
        var again = await renter.PostAsync($"/api/bookings/{booking.Id}/claim/accept");
        var earnings = await (await owner.Http.GetAsync("/api/me/earnings")).ReadAsync<EarningsDto>();

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.Forbidden), (tooMuch.StatusCode, vague.StatusCode, byRenter.StatusCode));
        Assert.Equal(("Disputed", 300m, (decimal?)null), (claimed.Status, claimed.Claim!.Amount, claimed.Claim.Taken));
        Assert.True(renterSees.Claim!.CanAnswer);
        Assert.Equal(("Completed", 300m, true, (decimal?)300m), (accepted.Status, accepted.Payment.DepositCharged, accepted.Payment.DepositSettled, accepted.Claim!.Taken));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        // 1,000 TL deposit, 300 taken: the provider keeps 300 and lets 700 go.
        Assert.Equal(30000L, await factory.ScalarAsync<long>("SELECT captured FROM fakepay.holds WHERE id = (SELECT deposit_hold_id FROM bookings WHERE id = @p0)", booking.Id));
        // Three days at 150 TL is 450, 405 of it the owner's; the damage is all theirs.
        Assert.Equal((405m + 300m, 405m + 300m), (earnings.Owed, earnings.Earned));
        Assert.Contains(earnings.Lines, l => l.Kind == "DamageCharged" && l.Amount == 300m);
    }

    [Fact]
    public async Task A_rejected_claim_goes_to_an_administrator_who_sees_the_photos_and_decides()
    {
        var (owner, renter, booking) = await ReturnedAsync();
        using var _ = owner;
        using var __ = renter;
        using var stranger = await factory.RegisterAsync();
        var damage = await (await UploadAsync(owner, booking.Id, "Damage")).ReadAsync<HandoverPhotoDto>();
        (await owner.PostAsync($"/api/bookings/{booking.Id}/claim", new { amount = 400m, note = "Motor yanık kokuyor, çalışmıyor." })).EnsureSuccessStatusCode();
        var rejected = await (await renter.PostAsync($"/api/bookings/{booking.Id}/claim/reject", new { note = "Teslim ederken çalışıyordu, videosu var." })).ReadAsync<BookingDto>();
        using var books = await factory.RegisterAsync("Destek", ApiFactory.SupportEmail);

        var refused = await stranger.Http.GetAsync("/api/admin/disputes");
        var queue = await (await books.Http.GetAsync("/api/admin/disputes")).ReadAsync<List<DisputeDto>>();
        var detail = await (await books.Http.GetAsync($"/api/admin/bookings/{booking.Id}")).ReadAsync<BookingDto>();
        var photo = await books.Http.GetAsync(damage.Url);
        var strangerPhoto = await stranger.Http.GetAsync(damage.Url);
        var renterPhoto = await renter.Http.GetAsync(damage.ThumbUrl);
        var overClaim = await books.PostAsync($"/api/admin/bookings/{booking.Id}/resolve", new { amount = 401m });
        var decided = await (await books.PostAsync($"/api/admin/bookings/{booking.Id}/resolve", new { amount = 150m, note = "Yarı yarıya." })).ReadAsync<BookingDto>();

        Assert.Equal(("Disputed", "Rejected", false), (rejected.Status, rejected.Claim!.Answer, rejected.Claim.CanAnswer));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var item = Assert.Single(queue, d => d.BookingId == booking.Id);
        Assert.Equal(("Rejected", 400m, 1), (item.Answer, item.Claim, item.Photos));
        Assert.Equal((false, (string?)null), (detail.IsOwner, detail.Handover.PickupCode));
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Contains("private", photo.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, strangerPhoto.StatusCode);
        Assert.Equal(HttpStatusCode.OK, renterPhoto.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, overClaim.StatusCode);
        Assert.Equal(("Completed", 150m, "Yarı yarıya."), (decided.Status, decided.Payment.DepositCharged, decided.Reason));
    }

    [Fact]
    public async Task Condition_photos_are_taken_at_the_right_moments()
    {
        var (owner, renter, booking) = await RentedAsync();
        using var _ = owner;
        using var __ = renter;

        var pickup = await UploadAsync(renter, booking.Id, "Pickup");
        var tooEarly = await UploadAsync(renter, booking.Id, "Return");
        var damageBefore = await UploadAsync(owner, booking.Id, "Damage");
        var nonsense = await UploadAsync(owner, booking.Id, "Later");
        var shown = await GetAsync(owner, booking.Id);

        Assert.Equal(HttpStatusCode.Created, pickup.StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.BadRequest), (tooEarly.StatusCode, damageBefore.StatusCode, nonsense.StatusCode));
        var photo = Assert.Single(shown.Photos);
        Assert.Equal(("Pickup", renter.Name), (photo.Stage, photo.By));
    }

    [Fact]
    public async Task Both_sides_review_once_and_the_stars_add_up_where_others_see_them()
    {
        var (owner, renter, booking) = await ReturnedAsync();
        using var _ = owner;
        using var __ = renter;
        var before = await owner.PostAsync($"/api/bookings/{booking.Id}/review", new { stars = 5 });
        (await owner.PostAsync($"/api/bookings/{booking.Id}/all-good")).EnsureSuccessStatusCode();

        var fromRenter = await renter.PostAsync($"/api/bookings/{booking.Id}/review", new { stars = 5, comment = "Tertemiz ve zamanında." });
        var twice = await renter.PostAsync($"/api/bookings/{booking.Id}/review", new { stars = 1 });
        var six = await owner.PostAsync($"/api/bookings/{booking.Id}/review", new { stars = 6 });
        var fromOwner = await owner.PostAsync($"/api/bookings/{booking.Id}/review", new { stars = 4, comment = "Biraz geç getirdi." });
        using var anonymous = factory.CreateClient();
        var listing = await (await anonymous.GetAsync($"/api/listings/{booking.Listing.Id}")).ReadAsync<ListingDto>();
        var listingReviews = await (await anonymous.GetAsync($"/api/listings/{booking.Listing.Id}/reviews")).ReadAsync<PageDto<ReviewDto>>();
        var aboutRenter = await (await owner.Http.GetAsync($"/api/users/{renter.Id}/reviews")).ReadAsync<PageDto<ReviewDto>>();
        var view = await GetAsync(renter, booking.Id);

        Assert.Equal(HttpStatusCode.Conflict, before.StatusCode);
        Assert.Equal(HttpStatusCode.Created, fromRenter.StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, "already_reviewed"), (twice.StatusCode, await twice.ProblemCodeAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, six.StatusCode);
        Assert.Equal(HttpStatusCode.Created, fromOwner.StatusCode);
        Assert.Equal(((double?)5.0, 1), (listing.Rating, listing.RatingCount));
        Assert.Equal([("Tertemiz ve zamanında.", renter.Name)], listingReviews.Items.Select(r => (r.Comment, r.AuthorName)));
        Assert.Equal([4], aboutRenter.Items.Select(r => r.Stars));
        Assert.Equal(((double?)4.0, 1), (view.Renter.Rating, view.Renter.RatingCount));
        Assert.Equal((false, 5, 4), (view.Reviews.CanReview, view.Reviews.Mine!.Stars, view.Reviews.Theirs!.Stars));
    }

    [Fact]
    public async Task The_clock_finishes_what_nobody_answered()
    {
        var (owner, renter, quiet) = await ReturnedAsync();
        var (owner2, renter2, unanswered) = await ReturnedAsync();
        var (owner3, renter3, noShow) = await RentedAsync(days: 1);
        (await owner2.PostAsync($"/api/bookings/{unanswered.Id}/claim", new { amount = 250m, note = "Kablo ezilmiş, değişmesi gerek." })).EnsureSuccessStatusCode();
        foreach (var person in new[] { owner, renter, owner2, renter2, owner3, renter3 })
        {
            person.Dispose();
        }

        factory.Clock.Advance(TimeSpan.FromHours(49));
        var first = await factory.SweepAsync();
        var afterTwoDays = (await StateAsync(quiet.Id), await StateAsync(unanswered.Id), await StateAsync(noShow.Id));
        factory.Clock.Advance(TimeSpan.FromHours(24));
        await factory.SweepAsync();

        Assert.True(first.Lapsed >= 2);
        Assert.Equal((("Completed", 0L, true), ("Disputed", 0L, false), ("Completed", 0L, true)), afterTwoDays);
        Assert.Equal(("Completed", 25000L, true), await StateAsync(unanswered.Id));
        Assert.Equal("Released", await HoldStatusAsync(noShow.Id, "deposit"));
    }

    private async Task<(Person Owner, Person Renter, BookingDto Booking)> RentedAsync(int fromDay = 0, int days = 3)
    {
        var owner = await factory.RegisterAsync("Mehmet Usta");
        var renter = await factory.RegisterAsync("Ayşe Kaya");
        var listing = await owner.ListAsync(TestApi.Body(instantBook: true));
        var booking = await renter.BookAsync(listing.Id, factory.Day(fromDay), factory.Day(fromDay + days));
        Assert.Equal("Confirmed", booking.Status);
        return (owner, renter, booking);
    }

    private async Task<(Person Owner, Person Renter, BookingDto Booking)> ReturnedAsync()
    {
        var (owner, renter, booking) = await RentedAsync();
        var pickup = (await GetAsync(renter, booking.Id)).Handover.PickupCode;
        (await owner.PostAsync($"/api/bookings/{booking.Id}/pickup", new { code = pickup })).EnsureSuccessStatusCode();
        var back = (await GetAsync(owner, booking.Id)).Handover.ReturnCode;
        (await renter.PostAsync($"/api/bookings/{booking.Id}/return", new { code = back })).EnsureSuccessStatusCode();
        return (owner, renter, booking);
    }

    private static async Task<BookingDto> GetAsync(Person person, Guid bookingId) =>
        await (await person.Http.GetAsync($"/api/bookings/{bookingId}")).ReadAsync<BookingDto>();

    private static async Task<HttpResponseMessage> UploadAsync(Person person, Guid bookingId, string stage)
    {
        using var content = new ByteArrayContent(TestPhotos.Jpeg(400, 300, withGps: true));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return await person.Http.PostAsync($"/api/bookings/{bookingId}/photos?stage={stage}", content);
    }

    private async Task<(string Status, long Damage, bool DepositSettled)> StateAsync(Guid bookingId) =>
        (await factory.ScalarAsync<string>("SELECT status FROM bookings WHERE id = @p0", bookingId) ?? string.Empty,
         await factory.ScalarAsync<long>("SELECT damage_minor FROM bookings WHERE id = @p0", bookingId),
         await factory.ScalarAsync<bool>("SELECT deposit_settled_at IS NOT NULL FROM bookings WHERE id = @p0", bookingId));

    private Task<string?> HoldStatusAsync(Guid bookingId, string which) =>
        factory.ScalarAsync<string>($"SELECT status FROM fakepay.holds WHERE id = (SELECT {which}_hold_id FROM bookings WHERE id = @p0)", bookingId);

    private static string Other(string code) => code == "000000" ? "111111" : "000000";
}
