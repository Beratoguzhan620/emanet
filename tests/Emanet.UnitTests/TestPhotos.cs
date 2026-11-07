using SkiaSharp;

namespace Emanet.UnitTests;

/// <summary>Photos like a phone's, made in memory, with an EXIF block written by hand when asked.</summary>
public static class TestPhotos
{
    public static byte[] Jpeg(int width, int height, ushort? orientation = null, bool withGps = false)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(31, 92, 115));
            // A marker in the top-left corner, to see which way the picture ends up.
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, width / 4f, height / 4f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var jpeg = data.ToArray();
        return orientation is null && !withGps ? jpeg : WithExif(jpeg, orientation ?? 1, withGps);
    }

    public static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Transparent);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static bool HasExif(byte[] jpeg) =>
        jpeg.AsSpan().IndexOf("Exif\0\0"u8) >= 0;

    /// <summary>
    /// Puts an APP1 EXIF segment right after the JPEG's start marker: orientation, and optionally a GPS block at
    /// 41° 19' N, 36° 16' E (Samsun).
    /// </summary>
    private static byte[] WithExif(byte[] jpeg, ushort orientation, bool withGps)
    {
        var tiff = new List<byte>();
        void U16(int v) { tiff.Add((byte)v); tiff.Add((byte)(v >> 8)); }
        void U32(long v) { for (var i = 0; i < 4; i++) tiff.Add((byte)(v >> (8 * i))); }

        // Little-endian TIFF header, first IFD at offset 8.
        tiff.AddRange("II"u8.ToArray());
        U16(42);
        U32(8);
        var ifd0Entries = withGps ? 2 : 1;
        var gpsIfd = 8 + 2 + (12 * ifd0Entries) + 4;
        U16(ifd0Entries);
        U16(0x0112); U16(3); U32(1); U16(orientation); U16(0);
        if (withGps)
        {
            U16(0x8825); U16(4); U32(1); U32(gpsIfd);
        }

        U32(0);
        if (withGps)
        {
            var values = gpsIfd + 2 + (12 * 4) + 4;
            U16(4);
            U16(0x0001); U16(2); U32(2); tiff.AddRange("N\0\0\0"u8.ToArray());
            U16(0x0002); U16(5); U32(3); U32(values);
            U16(0x0003); U16(2); U32(2); tiff.AddRange("E\0\0\0"u8.ToArray());
            U16(0x0004); U16(5); U32(3); U32(values + 24);
            U32(0);
            foreach (var (n, d) in new[] { (41, 1), (19, 1), (0, 1), (36, 1), (16, 1), (0, 1) })
            {
                U32(n);
                U32(d);
            }
        }

        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var length = payload.Length + 2;
        var segment = new byte[] { 0xFF, 0xE1, (byte)(length >> 8), (byte)length }.Concat(payload);
        return jpeg.Take(2).Concat(segment).Concat(jpeg.Skip(2)).ToArray();
    }
}
