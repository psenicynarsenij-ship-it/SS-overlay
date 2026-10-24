using System.IO;
using DotaHpAlert.Core;
using DotaHpAlert.Recognition;
namespace DotaHpAlert.App;

internal static class MonitorSession
{
    public static Task RunAsync(ApplicationSettings settings, string? demoDirectory,
        Func<MonitorUpdate, Task> report, CancellationToken cancellation)
    {
        var sequence = new[] { "healthy.png", "threshold.png", "low.png", "critical.png", "critical.png", "critical.png", "healthy.png" };
        var index = 0;
        ValueTask<ImageFrame> Capture(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(demoDirectory is null
                ? WindowsCapture.Capture(settings.WindowTitle, settings.Region)
                : ImageFrame.Load(Path.Combine(demoDirectory, sequence[index++ % sequence.Length])));
        }
        return RecognitionLoop.RunAsync(Path.Combine(AppContext.BaseDirectory, "tessdata"),
            demoDirectory is null ? settings : settings with { CaptureIntervalMs = 2000 },
            Capture, (update, _) => report(update), cancellation);
    }
}
