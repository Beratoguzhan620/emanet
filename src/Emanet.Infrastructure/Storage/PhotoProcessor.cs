using Emanet.Core;
using SkiaSharp;

namespace Emanet.Infrastructure.Storage;

/// <summary>A photo ready to store: a large and a small JPEG, without the camera's metadata.</summary>
public sealed record ProcessedPhoto(byte[] Full, byte[] Thumb, int Width, int Height);

/// <summary>
/// Turns an upload into the two sizes the app shows. Every photo is decoded to pixels and encoded again, so what is
/// stored is always a plain JPEG: nothing the uploader sent survives except the picture. In particular the EXIF
/// block, which on a phone photo holds where it was taken, never reaches storage.
/// </summary>
public static class PhotoProcessor
{
    public const int MaxUploadBytes = 10 * 1024 * 1024;
    public const int FullEdge = 1600;
    public const int ThumbWidth = 480;
    public const int ThumbHeight = 360;

    /// <summary>Decoding a huge canvas costs memory however small the file; anything past this is refused.</summary>
    private const long MaxPixels = 40_000_000;

    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    public static Task<ProcessedPhoto> ProcessAsync(byte[] upload, CancellationToken ct)
    {
        if (Sniff(upload) is null)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Photos are JPEG, PNG or WebP.");
        }

        ct.ThrowIfCancellationRequested();
        using var data = SKData.CreateCopy(upload);
        using var codec = SKCodec.Create(data) ?? throw Unreadable();
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "This photo is too large; send one under 40 megapixels.");
        }

        using var decoded = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var result = codec.GetPixels(decoded.Info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            throw Unreadable();
        }

        // Phones store photos sideways and say which way is up in the metadata; turn the pixels instead.
        using var upright = Orient(decoded, codec.EncodedOrigin);
        using var image = SKImage.FromBitmap(upright);

        var scale = Math.Min(1.0, (double)FullEdge / Math.Max(image.Width, image.Height));
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var full = Draw(image, new SKRect(0, 0, image.Width, image.Height), width, height, 82);

        // The thumbnail fills a 4:3 frame, cut from the middle of the photo.
        var target = (double)ThumbWidth / ThumbHeight;
        var source = (double)image.Width / image.Height > target
            ? Centred(image.Width, image.Height, (float)(image.Height * target), image.Height)
            : Centred(image.Width, image.Height, image.Width, (float)(image.Width / target));
        var thumb = Draw(image, source, ThumbWidth, ThumbHeight, 78);
        return Task.FromResult(new ProcessedPhoto(full, thumb, width, height));
    }

    /// <summary>The type the first bytes say, or null.</summary>
    public static string? Sniff(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };

    private static DomainException Unreadable() => new(ErrorCodes.ValidationFailed, "This file could not be read as a photo.");

    private static SKRect Centred(int width, int height, float w, float h) =>
        SKRect.Create((width - w) / 2, (height - h) / 2, w, h);

    private static byte[] Draw(SKImage image, SKRect source, int width, int height, int quality)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Could not allocate an image surface.");
        // JPEG has no transparency: a transparent PNG goes on white rather than black.
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawImage(image, source, new SKRect(0, 0, width, height), Sampling);
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, quality);
        return encoded.ToArray();
    }

    /// <summary>The pixels turned and mirrored so that the top of the picture is the top of the bitmap.</summary>
    private static SKBitmap Orient(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return bitmap.Copy();
        }

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (w, h) = swap ? (bitmap.Height, bitmap.Width) : (bitmap.Width, bitmap.Height);
        var result = new SKBitmap(new SKImageInfo(w, h, bitmap.ColorType, bitmap.AlphaType));
        using var canvas = new SKCanvas(result);
        // Each case maps a source pixel (x, y) to where it belongs; the last transform applies first.
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Translate(w, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.Translate(w, h); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, h); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(w, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(w, h); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, h); canvas.RotateDegrees(270); break;
        }

        canvas.DrawBitmap(bitmap, 0, 0);
        canvas.Flush();
        return result;
    }
}
