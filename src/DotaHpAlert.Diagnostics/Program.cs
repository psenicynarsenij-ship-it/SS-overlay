using System.Text.Json;
using System.Diagnostics;
using DotaHpAlert.Recognition;

try
{
    var tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata");
    if (args.Length == 0) { Console.Error.WriteLine("Usage: diagnostics <cropped-image> | --benchmark <fixtures/manifest.json> [report.json]"); return 2; }
    if (args[0] == "--stress")
    {
        var count = args.Length > 2 ? int.Parse(args[2]) : 500;
        if (count < 1 || count > 10000) throw new ArgumentException("Stress count must be 1–10000.");
        var image = ImageFrame.Load(args[1]);
        using var recognizer = new HpRecognizer(tessdata);
        for (var i = 0; i < 50; i++) recognizer.Recognize(image, DateTimeOffset.UtcNow);
        var process = Process.GetCurrentProcess(); process.Refresh();
        var managedBefore = GC.GetTotalMemory(forceFullCollection: true);
        process.Refresh();
        var before = process.PrivateMemorySize64;
        var samples = new List<object>();
        var elapsed = Stopwatch.StartNew();
        var invalid = 0;
        for (var i = 0; i < count; i++)
        {
            if (!recognizer.Recognize(image, DateTimeOffset.UtcNow).Reading.IsValid) invalid++;
            if ((i + 1) % 500 == 0)
            {
                elapsed.Stop();
                var managed = GC.GetTotalMemory(forceFullCollection: true); process.Refresh();
                samples.Add(new { Iteration = i + 1, ManagedBytes = managed, PrivateBytes = process.PrivateMemorySize64 });
                elapsed.Start();
            }
        }
        elapsed.Stop();
        var managedAfter = GC.GetTotalMemory(forceFullCollection: true); process.Refresh();
        Console.WriteLine(JsonSerializer.Serialize(new { Iterations = count, Invalid = invalid,
            MeanMs = elapsed.Elapsed.TotalMilliseconds / count,
            PrivateBytesBefore = before, PrivateBytesAfter = process.PrivateMemorySize64,
            ManagedBytesBefore = managedBefore, ManagedBytesAfter = managedAfter,
            WorkingSetBytes = process.WorkingSet64, Samples = samples }, new JsonSerializerOptions { WriteIndented = true }));
        return invalid == 0 ? 0 : 1;
    }
    if (args[0] != "--benchmark")
    {
        using var recognizer = new HpRecognizer(tessdata);
        var result = Read(recognizer, args[0], null);
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return result.Reading.IsValid ? 0 : 1;
    }
    var manifest = Path.GetFullPath(args[1]);
    var fixtures = JsonSerializer.Deserialize<List<Fixture>>(File.ReadAllText(manifest))!;
    var reports = new List<object>();
    foreach (var mode in Enum.GetValues<PreprocessingMode>())
    foreach (var psm in new[] { 7, 8, 13 })
    {
        var initialization = Stopwatch.StartNew();
        using var recognizer = new HpRecognizer(tessdata, new() { Preprocessing = mode, PageSegmentationMode = psm });
        initialization.Stop();
        var fullTimes = new List<double>();
        var results = new List<object>(); var times = new List<double>(); var correct = 0;
        foreach (var fixture in fixtures)
        {
            var file = Path.Combine(Path.GetDirectoryName(manifest)!, fixture.File);
            Read(recognizer, file, fixture.Region); // warm-up
            RecognitionResult? result = null;
            for (var i = 0; i < 10; i++)
            {
                var fullWatch = Stopwatch.StartNew();
                result = Read(recognizer, file, fixture.Region);
                fullTimes.Add(fullWatch.Elapsed.TotalMilliseconds);
                times.Add(result.Duration.TotalMilliseconds);
            }
            var passed = fixture.Hp is null ? !result!.Reading.IsValid :
                result!.Reading.IsValid && result.Reading.CurrentHp == fixture.Hp;
            if (passed) correct++;
            results.Add(new { fixture.File, Expected = fixture.Hp, result!.RawText, result.Reading, Passed = passed });
        }
        times.Sort(); fullTimes.Sort();
        var median = times[times.Count / 2]; var p95 = times[(int)((times.Count - 1) * .95)];
        Console.WriteLine($"{mode} PSM={psm}: {correct}/{fixtures.Count}, median={median:F2}ms p95={p95:F2}ms");
        reports.Add(new { Preprocessing = mode.ToString(), Psm = psm, Correct = correct, Total = fixtures.Count,
            MedianMs = median, P95Ms = p95, InitializationMs = initialization.Elapsed.TotalMilliseconds,
            DecodeAndOcrMedianMs = fullTimes[fullTimes.Count / 2],
            DecodeAndOcrP95Ms = fullTimes[(int)((fullTimes.Count - 1) * .95)],
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription, Results = results });
    }
    if (args.Length > 2) File.WriteAllText(args[2], JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception error) { Console.Error.WriteLine($"OCR error: {error.Message}"); return 2; }

static RecognitionResult Read(HpRecognizer recognizer, string path, DotaHpAlert.Core.CaptureRegion? region)
{
    var info = ImageFrame.Identify(path);
    return region is not null || info.Height > ImagePreprocessor.MaxHeight || info.Width > ImagePreprocessor.MaxWidth
        ? recognizer.RecognizeScreenshot(path, region ?? new(), DateTimeOffset.UtcNow)
        : recognizer.RecognizeFile(path, DateTimeOffset.UtcNow);
}

internal sealed record Fixture(string File, int? Hp, DotaHpAlert.Core.CaptureRegion? Region = null);
