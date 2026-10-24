using DotaHpAlert.Core;
using DotaHpAlert.Recognition;
using Xunit;
namespace DotaHpAlert.Tests;

public sealed class RecognitionLoopTests
{
    private static string Tessdata => Path.Combine(AppContext.BaseDirectory,"tessdata");
    private static ImageFrame Low => ImageFrame.Load(Path.Combine(AppContext.BaseDirectory,"fixtures/synthetic/low.png"));
    [Fact] public async Task CallbackIsAwaitedAndCancellationStopsFurtherFrames()
    {
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var captured = 0; var reported = 0;
        var image = Low;
        var task = RecognitionLoop.RunAsync(Tessdata, new() { CaptureIntervalMs = 100, Alerts = new() { InvokerEnabled = true } },
            _ => { captured++; return ValueTask.FromResult(image); },
            async (update, token) =>
            {
                Assert.Equal(AlertAction.Show,update.Decision!.Action);
                Assert.NotNull(update.PreviewPng);
                await Task.Yield();
                Assert.Equal(1,captured); reported++; source.Cancel();
            }, source.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1,captured); Assert.Equal(1,reported);
    }
    [Fact] public async Task CaptureFailureIsDiagnosedAndResetsActiveAlarm()
    {
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var captured = 0; var actions = new List<AlertAction>();
        var image = Low;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecognitionLoop.RunAsync(Tessdata,
            new() { CaptureIntervalMs = 100, Alerts = new() { InvokerEnabled = true } },
            _ => ++captured == 2 ? throw new InvalidOperationException("window lost") : ValueTask.FromResult(image),
            (update, _) =>
            {
                actions.Add(update.Decision!.Action);
                if (actions.Count == 2) { Assert.Equal("window lost", update.Message); Assert.Null(update.Recognition); }
                if (actions.Count == 3) source.Cancel();
                return Task.CompletedTask;
            }, source.Token));
        Assert.Equal(new[] { AlertAction.Show, AlertAction.Reset, AlertAction.Show }, actions);
    }
    [Fact] public async Task CancelledLoopDoesNotCaptureOrInitializeNativeEngine()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RecognitionLoop.RunAsync("missing-model", new(),
            _ => throw new Exception("must not capture"), (_,_) => throw new Exception("must not report"), source.Token));
    }
    [Fact] public async Task CallbackFailureTerminatesRatherThanStartingAnotherFrame()
    {
        var captured = 0; var image = Low;
        await Assert.ThrowsAsync<InvalidOperationException>(() => RecognitionLoop.RunAsync(Tessdata,new(),
            _ => { captured++; return ValueTask.FromResult(image); },
            (_,_) => throw new InvalidOperationException("consumer failed"), CancellationToken.None));
        Assert.Equal(1,captured);
    }
}
