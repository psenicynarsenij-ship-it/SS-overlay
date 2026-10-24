using System.Text.Json;
using DotaHpAlert.Core;
using DotaHpAlert.Recognition;
using Xunit;

namespace DotaHpAlert.Tests;

public sealed class RecognitionTests
{
    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static string Tessdata => Path.Combine(AppContext.BaseDirectory, "tessdata");
    [Theory]
    [InlineData("2298 / 2298", true, 2298)] [InlineData("59 /538", true, 59)]
    [InlineData("350", true, 350)] [InlineData("0 / 100", true, 0)]
    [InlineData(" 349 / 1200\n", true, 349)] [InlineData("800 / 100", false, 0)]
    [InlineData("59 / 0", false, 0)] [InlineData("SETTINGS", false, 0)]
    [InlineData("HP: 59", false, 0)] [InlineData("5O", false, 0)]
    [InlineData("-59", false, 0)] [InlineData("59.0", false, 0)]
    [InlineData("59 538", false, 0)] [InlineData("59 / 538 / 99", false, 0)]
    [InlineData("", false, 0)] [InlineData("100001", false, 0)]
    public void ParserRequiresWholeUnambiguousReading(string text, bool valid, int hp)
    {
        var actual = HpTextParser.TryParse(text, 100000, out var value);
        Assert.Equal(valid, actual);
        if (valid) Assert.Equal(hp, value);
    }
    public static IEnumerable<object[]> FixtureNames()
    {
        var fixtures = JsonSerializer.Deserialize<List<Fixture>>(File.ReadAllText(Path.Combine(Fixtures, "manifest.json")))!;
        return fixtures.Select(f => new object[] { f.File, f.Hp ?? -1, f.Region is not null });
    }
    [Theory] [MemberData(nameof(FixtureNames))]
    public void NativeOcrRecognizesFixturesAndRejectsNegatives(string file, int expected, bool screenshot)
    {
        using var recognizer = new HpRecognizer(Tessdata);
        var path = Path.Combine(Fixtures, file);
        var result = screenshot ? recognizer.RecognizeScreenshot(path, new(), DateTimeOffset.UnixEpoch) :
            recognizer.RecognizeFile(path, DateTimeOffset.UnixEpoch);
        Assert.Equal(expected >= 0, result.Reading.IsValid);
        if (expected >= 0) Assert.Equal(expected, result.Reading.CurrentHp);
        Assert.Equal(DateTimeOffset.UnixEpoch, result.Reading.MeasuredAt);
        Assert.InRange(result.Reading.Confidence, 0, 1);
        Assert.True(result.Duration > TimeSpan.Zero);
    }
    [Fact] public void RealScreenshotsFlowThroughAlertEngine()
    {
        using var recognizer = new HpRecognizer(Tessdata);
        var engine = new AlertEngine(new() { InvokerEnabled = true });
        var healthy = recognizer.RecognizeScreenshot(Path.Combine(Fixtures, "Dota 2 Screenshot 2026.10.24 - 17.55.17.90.png"), new(), DateTimeOffset.UtcNow);
        var low = recognizer.RecognizeScreenshot(Path.Combine(Fixtures, "Dota 2 Screenshot 2026.10.24 - 18.02.12.14.png"), new(), DateTimeOffset.UtcNow);
        Assert.Equal(AlertAction.None, engine.Evaluate(healthy.Reading).Action);
        Assert.Equal(AlertAction.Show, engine.Evaluate(low.Reading).Action);
        Assert.Equal(AlertAction.None, engine.Evaluate(low.Reading).Action);
        Assert.Equal(AlertAction.Reset, engine.Evaluate(healthy.Reading).Action);
    }
    [Fact] public void CroppingIsIndependentOfMapPixels()
    {
        var image = ImageFrame.Load(Path.Combine(Fixtures, "Dota 2 Screenshot 2026.10.24 - 18.02.12.14.png"));
        var region = new CaptureRegion(); var original = image.Crop(region);
        Array.Fill(image.Pixels, (byte)255, 0, 900 * image.Width * 4);
        var changed = image.Crop(region);
        Assert.Equal(original.Pixels, changed.Pixels);
        using var recognizer = new HpRecognizer(Tessdata);
        Assert.Equal(59, recognizer.Recognize(changed, DateTimeOffset.UtcNow).Reading.CurrentHp);
    }
    [Fact] public void NativeEngineCanBeReusedAndDisposed()
    {
        var image = ImageFrame.Load(Path.Combine(Fixtures, "synthetic/low.png"));
        var recognizer = new HpRecognizer(Tessdata);
        for (var i = 0; i < 30; i++) Assert.Equal(349, recognizer.Recognize(image, DateTimeOffset.UtcNow).Reading.CurrentHp);
        recognizer.Dispose(); recognizer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => recognizer.Recognize(image, DateTimeOffset.UtcNow));
    }
    [Fact] public void MissingModelHasActionableError() =>
        Assert.Throws<FileNotFoundException>(() => new HpRecognizer(Path.Combine(Fixtures, "missing")));
    [Fact] public void ImageBufferAndCropAreValidated()
    {
        Assert.Throws<ArgumentException>(() => new ImageFrame(3, 3, new byte[2]));
        var image = new ImageFrame(2, 2, new byte[16]);
        Assert.Throws<ArgumentException>(() => image.Crop(new(1, 0, 2, 2)));
        Assert.Throws<ArgumentException>(() => ImagePreprocessor.Process(new(1, 1, new byte[4]), PreprocessingMode.Otsu));
    }
    [Fact] public void PngRoundTripPreservesPixels()
    {
        var image = new ImageFrame(2, 2, Enumerable.Range(0,16).Select(i => (byte)i).ToArray());
        var path = Path.GetTempFileName();
        try { File.WriteAllBytes(path, image.ToPng()); Assert.Equal(image.Pixels, ImageFrame.Load(path).Pixels); }
        finally { File.Delete(path); }
    }
    [Theory] [InlineData(PreprocessingMode.Grayscale)] [InlineData(PreprocessingMode.BrightText)] [InlineData(PreprocessingMode.Otsu)]
    public void PreprocessorPadsWithWhiteAndBoundsPixelBuffer(PreprocessingMode mode)
    {
        var frame = new ImageFrame(4, 4, new byte[4*4*4]);
        var output = ImagePreprocessor.Process(frame, mode);
        Assert.Equal(36, output.Width); Assert.Equal(36, output.Height);
        Assert.Equal(output.Width * output.Height, output.Pixels.Length);
        Assert.All(output.Pixels.Take(output.Width * 10), p => Assert.Equal(255, p));
    }
    [Fact] public void InvalidOcrOptionsRejected()
    {
        Assert.Throws<ArgumentException>(() => new OcrOptions { MinimumConfidence = double.NaN }.Validate());
        Assert.Throws<ArgumentException>(() => new OcrOptions { PageSegmentationMode = 3 }.Validate());
    }
    private sealed record Fixture(string File, int? Hp, CaptureRegion? Region);
}
