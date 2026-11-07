using Emanet.Core;
using Emanet.Infrastructure.Storage;
using SkiaSharp;

namespace Emanet.UnitTests;

public sealed class PhotoProcessorTests
{
    [Fact]
    public async Task A_phone_photo_is_turned_upright_shrunk_and_stripped_of_its_location()
    {
        var upload = TestPhotos.Jpeg(3000, 2000, orientation: 6, withGps: true);
        Assert.True(TestPhotos.HasExif(upload));

        var processed = await PhotoProcessor.ProcessAsync(upload, CancellationToken.None);

        using var full = SKBitmap.Decode(processed.Full);
        using var thumb = SKBitmap.Decode(processed.Thumb);
        Assert.Equal((1067, 1600), (full.Width, full.Height));
        Assert.Equal((processed.Width, processed.Height), (full.Width, full.Height));
        Assert.False(TestPhotos.HasExif(processed.Full));
        Assert.False(TestPhotos.HasExif(processed.Thumb));
        Assert.Equal((480, 360), (thumb.Width, thumb.Height));
        // Turned a quarter clockwise, the red top-left corner of the sensor ends up at the top right.
        Assert.True(IsRed(full.GetPixel(full.Width - 20, 20)));
        Assert.False(IsRed(full.GetPixel(20, 20)));
    }

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(3, false, false)]
    [InlineData(8, false, true)]
    public async Task Every_orientation_ends_up_the_right_way(ushort orientation, bool redTopLeft, bool redBottomLeft)
    {
        var processed = await PhotoProcessor.ProcessAsync(TestPhotos.Jpeg(400, 300, orientation), CancellationToken.None);

        using var full = SKBitmap.Decode(processed.Full);
        Assert.Equal(redTopLeft, IsRed(full.GetPixel(10, 10)));
        Assert.Equal(redBottomLeft, IsRed(full.GetPixel(10, full.Height - 10)));
    }

    [Fact]
    public async Task Small_photos_are_not_blown_up_and_png_becomes_jpeg()
    {
        var processed = await PhotoProcessor.ProcessAsync(TestPhotos.Png(640, 480), CancellationToken.None);

        Assert.Equal((640, 480), (processed.Width, processed.Height));
        Assert.Equal("image/jpeg", PhotoProcessor.Sniff(processed.Full));
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A })]
    public async Task Anything_that_is_not_a_readable_image_is_refused(byte[] bytes)
    {
        var error = await Assert.ThrowsAsync<DomainException>(() => PhotoProcessor.ProcessAsync(bytes, CancellationToken.None));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
    }

    private static bool IsRed(SKColor c) => c.Red > 180 && c.Green < 90 && c.Blue < 90;
}
