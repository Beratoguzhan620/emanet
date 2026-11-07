using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Emanet.IntegrationTests.Support;
using Emanet.UnitTests;
using SkiaSharp;

namespace Emanet.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PhotoTests(ApiFactory factory)
{
    [Fact]
    public async Task A_photo_is_stored_in_two_sizes_without_where_it_was_taken()
    {
        using var owner = await factory.RegisterAsync();
        var listing = await owner.ListAsync();

        var response = await Upload(owner, listing.Id, Photo(2400, 1800, withGps: true));
        var photo = await response.ReadAsync<PhotoDto>();
        using var anonymous = factory.CreateClient();
        var full = await anonymous.GetByteArrayAsync(photo.Url);
        var thumb = await anonymous.GetByteArrayAsync(photo.ThumbUrl);
        var detail = await (await anonymous.GetAsync($"/api/listings/{listing.Id}")).ReadAsync<ListingDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal((1600, 1200), (photo.Width, photo.Height));
        using (var image = SKBitmap.Decode(full))
        {
            Assert.Equal((1600, 1200), (image.Width, image.Height));
        }

        using (var image = SKBitmap.Decode(thumb))
        {
            Assert.Equal((480, 360), (image.Width, image.Height));
        }

        Assert.False(TestPhotos.HasExif(full));

        Assert.Equal([photo.Id], detail.Photos.Select(p => p.Id));
    }

    [Fact]
    public async Task Only_the_owner_adds_photos_only_images_are_taken_and_there_are_at_most_eight()
    {
        using var owner = await factory.RegisterAsync();
        using var other = await factory.RegisterAsync();
        var listing = await owner.ListAsync();

        var stranger = await Upload(other, listing.Id, Photo(400, 300));
        var text = await Upload(owner, listing.Id, "not an image at all"u8.ToArray());
        var fake = await Upload(owner, listing.Id, [0xFF, 0xD8, 0xFF, 0x00, 0x01, 0x02]);
        for (var i = 0; i < 8; i++)
        {
            (await Upload(owner, listing.Id, Photo(400, 300))).EnsureSuccessStatusCode();
        }

        var ninth = await Upload(owner, listing.Id, Photo(400, 300));

        Assert.Equal(HttpStatusCode.Forbidden, stranger.StatusCode);
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (text.StatusCode, fake.StatusCode));
        Assert.Equal(HttpStatusCode.Conflict, ninth.StatusCode);
    }

    [Fact]
    public async Task Photos_are_reordered_removed_and_hidden_with_their_listing()
    {
        using var owner = await factory.RegisterAsync();
        var listing = await owner.ListAsync();
        var a = await (await Upload(owner, listing.Id, Photo(400, 300))).ReadAsync<PhotoDto>();
        var b = await (await Upload(owner, listing.Id, Photo(400, 300))).ReadAsync<PhotoDto>();
        var c = await (await Upload(owner, listing.Id, Photo(400, 300))).ReadAsync<PhotoDto>();

        (await owner.Http.PutAsJsonAsync($"/api/listings/{listing.Id}/photos/order", new { photoIds = new[] { c.Id, a.Id, b.Id } })).EnsureSuccessStatusCode();
        var partial = await owner.Http.PutAsJsonAsync($"/api/listings/{listing.Id}/photos/order", new { photoIds = new[] { c.Id, a.Id } });
        (await owner.Http.DeleteAsync($"/api/listings/{listing.Id}/photos/{a.Id}")).EnsureSuccessStatusCode();
        var detail = await (await owner.Http.GetAsync($"/api/listings/{listing.Id}")).ReadAsync<ListingDto>();
        using var anonymous = factory.CreateClient();
        var deleted = await anonymous.GetAsync(a.Url);
        (await owner.Http.DeleteAsync($"/api/listings/{listing.Id}")).EnsureSuccessStatusCode();
        var afterRemoval = await anonymous.GetAsync(c.ThumbUrl);

        Assert.Equal([c.Id, b.Id], detail.Photos.Select(p => p.Id));
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.False(factory.Files.Has($"listings/{listing.Id}/{a.Id}.jpg"));
        Assert.Equal(HttpStatusCode.NotFound, afterRemoval.StatusCode);
    }

    [Fact]
    public async Task Search_results_show_the_cover_photo()
    {
        using var owner = await factory.RegisterAsync();
        var listing = await owner.ListAsync(TestApi.Body(title: $"Kayak takımı {Guid.NewGuid():N}"[..30], category: "Sports"));
        var first = await (await Upload(owner, listing.Id, Photo(400, 300))).ReadAsync<PhotoDto>();
        await Upload(owner, listing.Id, Photo(400, 300));
        using var anonymous = factory.CreateClient();

        var found = await (await anonymous.GetAsync($"/api/listings?q={Uri.EscapeDataString(listing.Title)}")).ReadAsync<PageDto<ListingSummaryDto>>();

        Assert.Equal((first.ThumbUrl, 2), (found.Items.Single().ThumbUrl, found.Items.Single().Photos));
    }

    private static async Task<HttpResponseMessage> Upload(Person person, Guid listingId, byte[] bytes)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return await person.Http.PostAsync($"/api/listings/{listingId}/photos", content);
    }

    private static byte[] Photo(int width, int height, bool withGps = false) => TestPhotos.Jpeg(width, height, withGps: withGps);
}
