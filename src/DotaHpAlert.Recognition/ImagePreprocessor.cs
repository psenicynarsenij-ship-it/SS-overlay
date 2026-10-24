namespace DotaHpAlert.Recognition;

public sealed record GrayscaleFrame(int Width, int Height, byte[] Pixels);
public static class ImagePreprocessor
{
    public const int MaxWidth = 2048, MaxHeight = 512;
    public static GrayscaleFrame Process(ImageFrame source, PreprocessingMode mode)
    {
        if (source.Width < 2 || source.Height < 2 || source.Width > MaxWidth || source.Height > MaxHeight)
            throw new ArgumentException("Select a small HP text region (2–2048 by 2–512 pixels).");
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var scale = Math.Clamp(64.0 / source.Height, 1, 4);
        var width = (int)(source.Width * scale); var height = (int)(source.Height * scale);
        var gray = new byte[width * height]; var histogram = new int[256];
        // Bilinear resampling to retain antialiased glyph edges without a large imaging framework.
        double Luma(int x, int y)
        {
            var i = (y * source.Width + x) * 4;
            return .299 * source.Pixels[i] + .587 * source.Pixels[i + 1] + .114 * source.Pixels[i + 2];
        }
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sx = Math.Clamp((x + .5) / scale - .5, 0, source.Width - 1);
            var sy = Math.Clamp((y + .5) / scale - .5, 0, source.Height - 1);
            var x0 = (int)sx; var y0 = (int)sy; var x1 = Math.Min(x0 + 1, source.Width - 1); var y1 = Math.Min(y0 + 1, source.Height - 1);
            var dx = sx - x0; var dy = sy - y0;
            var v = (byte)Math.Clamp((int)Math.Round((1-dy)*((1-dx)*Luma(x0,y0)+dx*Luma(x1,y0)) + dy*((1-dx)*Luma(x0,y1)+dx*Luma(x1,y1))), 0, 255);
            gray[y * width + x] = v; histogram[v]++;
        }
        var threshold = mode == PreprocessingMode.Otsu ? Otsu(histogram, gray.Length) : 170;
        var outputWidth = width + 20; var outputHeight = height + 20;
        var output = new byte[outputWidth * outputHeight]; Array.Fill(output, (byte)255);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var v = gray[y * width + x];
            output[(y + 10) * outputWidth + x + 10] = mode == PreprocessingMode.Grayscale ? (byte)(255 - v) : v > threshold ? (byte)0 : (byte)255;
        }
        return new(outputWidth, outputHeight, output);
    }
    private static int Otsu(int[] hist, int total)
    {
        double sum = 0, backgroundSum = 0, best = -1;
        var background = 0; var threshold = 0;
        for (var i = 0; i < 256; i++) sum += i * (double)hist[i];
        for (var i = 0; i < 256; i++)
        {
            background += hist[i]; if (background == 0) continue;
            var foreground = total - background; if (foreground == 0) break;
            backgroundSum += i * (double)hist[i];
            var diff = backgroundSum / background - (sum - backgroundSum) / foreground;
            var score = background * (double)foreground * diff * diff;
            if (score > best) { best = score; threshold = i; }
        }
        return threshold;
    }
}
