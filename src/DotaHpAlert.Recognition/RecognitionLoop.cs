using System.Diagnostics;
using DotaHpAlert.Core;
namespace DotaHpAlert.Recognition;

public sealed record MonitorUpdate(RecognitionResult? Recognition, AlertDecision? Decision,
    byte[]? PreviewPng, TimeSpan TotalDuration, string Message);

/// <summary>Serial capture/OCR/decision pipeline. The caller must await shutdown before restarting.</summary>
public static class RecognitionLoop
{
    public static async Task RunAsync(string tessdata, ApplicationSettings settings,
        Func<CancellationToken, ValueTask<ImageFrame>> capture,
        Func<MonitorUpdate, CancellationToken, Task> report,
        CancellationToken cancellation, TimeProvider? clock = null)
    {
        settings.Validate();
        cancellation.ThrowIfCancellationRequested();
        using var recognizer = new HpRecognizer(tessdata, new() { MinimumConfidence = settings.MinimumConfidence });
        var engine = new AlertEngine(settings.Alerts, clock);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var watch = Stopwatch.StartNew();
            MonitorUpdate update;
            try
            {
                var image = await capture(cancellation);
                cancellation.ThrowIfCancellationRequested();
                var result = recognizer.Recognize(image, (clock ?? TimeProvider.System).GetUtcNow());
                cancellation.ThrowIfCancellationRequested();
                var decision = engine.Evaluate(result.Reading);
                var preview = image.ToPng();
                watch.Stop();
                update = new(result, decision, preview, watch.Elapsed, result.Diagnostic);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                var reset = engine.UpdateSettings(settings.Alerts with { InvokerEnabled = false });
                engine.UpdateSettings(settings.Alerts);
                update = new(null, reset, null, watch.Elapsed, error.Message);
            }
            // Backpressure: one frame at a time, including delivery. Callback failures terminate the loop.
            await report(update, cancellation);
            cancellation.ThrowIfCancellationRequested();
            var delay = Math.Max(1, settings.CaptureIntervalMs - (int)Math.Min(watch.ElapsedMilliseconds, int.MaxValue));
            await Task.Delay(delay, cancellation);
        }
    }
}
