using StbImageSharp;
using DotaHpAlert.Core;
namespace DotaHpAlert.Recognition;

/// <summary>Managed, tightly packed RGBA pixels. No native resource ownership.</summary>
public sealed class ImageFrame
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public ImageFrame(int width, int height, byte[] pixels)
    {
        if (width < 1 || height < 1 || (long)width * height > 33_177_600 || pixels.LongLength != (long)width * height * 4)
            throw new ArgumentException("Invalid RGBA image buffer.");
        Width = width; Height = height; Pixels = pixels;
    }
    public static (int Width, int Height) Identify(string path)
    {
        using var stream = File.OpenRead(path);
        var info = ImageInfo.FromStream(stream) ?? throw new InvalidDataException("Unsupported or corrupt image.");
        return (info.Width, info.Height);
    }
    public static ImageFrame Load(string path)
    {
        var info = Identify(path);
        if (info.Width < 1 || info.Height < 1 || info.Width > 7680 || info.Height > 4320 || (long)info.Width * info.Height > 33_177_600)
            throw new ArgumentException("Image exceeds the supported 8K limit.");
        using var stream = File.OpenRead(path);
        var decoded = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return new(decoded.Width, decoded.Height, decoded.Data);
    }
    public ImageFrame Crop(CaptureRegion region)
    {
        region.Validate();
        if ((long)region.X + region.Width > Width || (long)region.Y + region.Height > Height)
            throw new ArgumentException("HP region lies outside the image.");
        var data = new byte[region.Width * region.Height * 4];
        for (var y = 0; y < region.Height; y++)
            Buffer.BlockCopy(Pixels, ((region.Y + y) * Width + region.X) * 4, data, y * region.Width * 4, region.Width * 4);
        return new(region.Width, region.Height, data);
    }
    public byte[] ToPng()
    {
        using var stream = new MemoryStream();
        new StbImageWriteSharp.ImageWriter().WritePng(Pixels, Width, Height,
            StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        return stream.ToArray();
    }
}
