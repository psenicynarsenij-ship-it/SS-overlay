using System.Diagnostics;
using System.Runtime.InteropServices;
namespace DotaHpAlert.Recognition;

/// <summary>Not thread-safe. Own and dispose on the same serial OCR worker.</summary>
public sealed class HpRecognizer : IDisposable
{
    private IntPtr handle;
    private readonly OcrOptions options;
    public HpRecognizer(string tessdataDirectory, OcrOptions? options = null)
    {
        this.options = options ?? new(); this.options.Validate();
        if (!File.Exists(Path.Combine(tessdataDirectory, "eng.traineddata")))
            throw new FileNotFoundException("Missing tessdata/eng.traineddata. Restore application assets.");
        handle = NativeTesseract.TessBaseAPICreate();
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Cannot allocate Tesseract.");
        try
        {
            if (NativeTesseract.TessBaseAPIInit3(handle, tessdataDirectory, "eng") != 0)
                throw new InvalidOperationException("Tesseract initialization failed.");
            NativeTesseract.TessBaseAPISetPageSegMode(handle, this.options.PageSegmentationMode);
            // Do not whitelist characters: suppressing letters can turn unrelated text into false HP.
            NativeTesseract.TessBaseAPISetVariable(handle, "user_defined_dpi", "150");
        }
        catch { Dispose(); throw; }
    }
    public RecognitionResult RecognizeFile(string path, DateTimeOffset measuredAt)
    {
        var info = ImageFrame.Identify(path);
        if (info.Width > ImagePreprocessor.MaxWidth || info.Height > ImagePreprocessor.MaxHeight)
            throw new ArgumentException("Image is too large: crop to the HP region first.");
        var image = ImageFrame.Load(path);
        return Recognize(image, measuredAt);
    }
    public RecognitionResult RecognizeScreenshot(string path, DotaHpAlert.Core.CaptureRegion region, DateTimeOffset measuredAt)
    {
        region.Validate();
        var info = ImageFrame.Identify(path);
        if ((long)info.Width * info.Height > 33_177_600 || info.Width > 7680 || info.Height > 4320)
            throw new ArgumentException("Screenshot exceeds the supported 8K limit.");
        if ((long)region.X + region.Width > info.Width || (long)region.Y + region.Height > info.Height)
            throw new ArgumentException("HP region lies outside the screenshot.");
        var image = ImageFrame.Load(path);
        var crop = image.Crop(region);
        return Recognize(crop, measuredAt);
    }
    public RecognitionResult Recognize(ImageFrame image, DateTimeOffset measuredAt)
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
        var watch = Stopwatch.StartNew();
        var prepared = ImagePreprocessor.Process(image, options.Preprocessing);
        var pixels = prepared.Pixels;
        var text = ""; double confidence = 0;
        try
        {
            NativeTesseract.TessBaseAPISetImage(handle, pixels, prepared.Width, prepared.Height, 1, prepared.Width);
            var nativeText = NativeTesseract.TessBaseAPIGetUTF8Text(handle);
            try { text = Marshal.PtrToStringUTF8(nativeText)?.Trim() ?? ""; }
            finally { if (nativeText != IntPtr.Zero) NativeTesseract.TessDeleteText(nativeText); }
            confidence = Math.Clamp(NativeTesseract.TessBaseAPIMeanTextConf(handle) / 100.0, 0, 1);
        }
        finally { NativeTesseract.TessBaseAPIClear(handle); }
        var parsed = HpTextParser.TryParse(text, options.MaxHp, out var hp);
        var valid = parsed && confidence >= options.MinimumConfidence;
        watch.Stop();
        return new(new(hp, measuredAt, confidence, valid), text, watch.Elapsed,
            !parsed ? "Unrecognized HP text" : !valid ? "Low OCR confidence" : "OK");
    }
    public void Dispose()
    {
        if (handle == IntPtr.Zero) return;
        NativeTesseract.TessBaseAPIDelete(handle); handle = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }
    ~HpRecognizer() => Dispose();
}
