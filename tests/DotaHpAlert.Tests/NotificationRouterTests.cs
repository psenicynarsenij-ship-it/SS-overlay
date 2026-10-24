using DotaHpAlert.Core;
using Xunit;
namespace DotaHpAlert.Tests;

public sealed class NotificationRouterTests
{
    private readonly FakeOverlay overlay = new();
    private readonly FakeSound sound = new();
    private readonly List<NotificationEvent> events = new();
    private static readonly NotificationOptions Options = new(30, 40, .7);
    private static AlertDecision Alert(AlertAction action = AlertAction.Show, bool visual = true, bool audio = true) =>
        new(action, AlertState.Active, AlertReason.ThresholdCrossed, audio, visual);
    private NotificationRouter Router() => new(overlay, sound, events.Add);

    [Theory] [InlineData(AlertAction.Show)] [InlineData(AlertAction.Repeat)]
    public void RoutesBothChannelsWithOrderedDiagnostics(AlertAction action)
    {
        Router().Route(Alert(action), 59, Options, true);
        Assert.Equal(1, overlay.Calls); Assert.Equal(59,overlay.Hp);
        Assert.Equal((30d,40d),overlay.Position); Assert.Equal(.7,sound.Volume);
        Assert.Equal(new[] { NotificationEventKind.EngineIssued, NotificationEventKind.OverlayRequested,
            NotificationEventKind.OverlayShown, NotificationEventKind.SoundRequested, NotificationEventKind.SoundStarted },events.Select(e=>e.Kind));
    }
    [Fact] public void CooldownDoesNotRedeliverOrDismissExistingNotification()
    {
        var router = Router(); router.Route(Alert(),59,Options,true);
        router.Route(new(AlertAction.None, AlertState.Active, AlertReason.Cooldown),59,Options,true);
        Assert.Equal(1,overlay.Calls); Assert.Equal(0,overlay.Dismissals); Assert.Equal(1,sound.Calls);
    }
    [Fact] public void InvalidReadingDoesNotHideAnAlreadyDeliveredOverlay()
    {
        var router = Router(); router.Route(Alert(),59,Options,true);
        router.Route(new(AlertAction.None, AlertState.Active, AlertReason.InvalidReading),0,Options,true);
        Assert.Equal(0,overlay.Dismissals); Assert.Equal(1,sound.Calls);
    }
    [Fact] public void SuppressionIsLoggedAndNeverCallsChannels()
    {
        Router().Route(Alert(),59,Options,false);
        Assert.Equal(new[] { NotificationEventKind.EngineIssued, NotificationEventKind.DeliverySuppressed },events.Select(e=>e.Kind));
        Assert.Equal(0,overlay.Calls); Assert.Equal(0,sound.Calls);
    }
    [Theory] [InlineData(true,false)] [InlineData(false,true)] [InlineData(false,false)]
    public void HonorsPerDecisionChannelFlags(bool visual, bool audio)
    {
        Router().Route(Alert(visual:visual,audio:audio),59,Options,true);
        Assert.Equal(visual?1:0,overlay.Calls); Assert.Equal(audio?1:0,sound.Calls);
        Assert.Contains(events,e=>e.Kind==NotificationEventKind.EngineIssued && e.Message.Contains($"visual={visual}"));
    }
    [Fact] public void OverlayFailureDoesNotPreventSoundOrEscapeToOcrLoop()
    {
        overlay.Error = new InvalidOperationException("overlay failed");
        Router().Route(Alert(),59,Options,true);
        Assert.Equal(1,sound.Calls);
        Assert.Contains(events,e=>e.Kind==NotificationEventKind.OverlayError && e.Message.Contains("overlay failed"));
        Assert.DoesNotContain(events,e=>e.Kind==NotificationEventKind.OverlayShown);
        Assert.Contains(events,e=>e.Kind==NotificationEventKind.SoundStarted);
    }
    [Fact] public void SynchronousSoundFailureIsDiagnosedAndCanRetry()
    {
        var router = Router(); sound.Error = new IOException("device unavailable");
        router.Route(Alert(),59,Options,true);
        Assert.Equal(1,overlay.Calls);
        Assert.Contains(events,e=>e.Kind==NotificationEventKind.SoundError && e.Message.Contains("device unavailable"));
        Assert.DoesNotContain(events,e=>e.Kind==NotificationEventKind.SoundStarted);
        sound.Error = null; router.Route(Alert(AlertAction.Repeat),59,Options,true);
        Assert.Equal(2,sound.Calls);
        Assert.Contains(events,e=>e.Kind==NotificationEventKind.SoundStarted);
    }
    [Fact] public void RepeatedRequestsNeverOverlapSound()
    {
        var router = Router(); router.Test(Options); router.Test(Options);
        Assert.Equal(1,sound.Calls); Assert.Equal(2,overlay.Calls);
        Assert.Contains(events,e=>e.Kind==NotificationEventKind.SoundSkippedBusy);
        sound.Finish(null); router.Test(Options); Assert.Equal(2,sound.Calls);
    }
    [Fact] public void AsynchronousPlaybackFailureReleasesGateAndIsDiagnosed()
    {
        var router = Router(); router.Test(Options);
        sound.Finish(new IOException("output disconnected")); router.Test(Options);
        Assert.Equal(2,sound.Calls);
        Assert.Contains(events,e=>e.Kind==NotificationEventKind.SoundError && e.Message.Contains("output disconnected"));
    }
    [Fact] public void OldCompletionCannotReleaseNewPlaybackAfterStop()
    {
        var router = Router(); router.Test(Options); var old = sound.Completion!;
        router.Stop(); router.Test(Options); old(null); router.Test(Options);
        Assert.Equal(2,sound.Calls); Assert.Equal(1,sound.Stops);
        Assert.Contains(events,e=>e.Kind==NotificationEventKind.SoundSkippedBusy);
    }
    [Fact] public void ResetStopsBothChannelsEvenWhenDeliveryIsDisabled()
    {
        var router = Router(); router.Test(Options);
        router.Route(new(AlertAction.Reset,AlertState.Idle,AlertReason.ProfileDisabled),0,Options,false);
        Assert.Equal(1,overlay.Dismissals); Assert.Equal(1,sound.Stops);
        router.Test(Options); Assert.Equal(2,sound.Calls);
    }
    [Fact] public void OverlayDismissFailureDoesNotPreventSoundStop()
    {
        overlay.DismissError = new IOException("dismiss failed"); Router().Stop();
        Assert.Equal(1,sound.Stops);
        Assert.Contains(events,e=>e.Kind==NotificationEventKind.OverlayDismissError);
    }
    [Fact] public void SoundStopFailureIsDiagnosed()
    {
        sound.StopError = new IOException("stop failed"); Router().Stop();
        Assert.Contains(events,e=>e.Kind==NotificationEventKind.SoundError);
    }
    [Fact] public void FailedStopBlocksAnotherSoundUntilSuccessfulCleanup()
    {
        var router=Router(); router.Test(Options);
        sound.StopError=new IOException("stop failed"); router.Stop(); router.Test(Options);
        Assert.Equal(1,sound.Calls);
        sound.StopError=null; router.Stop(); router.Test(Options);
        Assert.Equal(2,sound.Calls);
    }
    [Fact] public void EngineShowCooldownRepeatAndResetReachExpectedChannels()
    {
        var clock=new TestClock();
        var engine=new AlertEngine(new() { InvokerEnabled=true },clock);
        var router=Router(); var reading=new HealthReading(59,DateTimeOffset.UnixEpoch,1,true);
        router.Route(engine.Evaluate(reading),59,Options,true);
        router.Route(engine.Evaluate(reading),59,Options,true);
        sound.Finish(null); clock.Seconds=5;
        router.Route(engine.Evaluate(reading),59,Options,true);
        router.Route(engine.Evaluate(reading with { CurrentHp=400 }),400,Options,true);
        Assert.Equal(2,overlay.Calls); Assert.Equal(2,sound.Calls);
        Assert.Equal(1,overlay.Dismissals); Assert.Equal(1,sound.Stops);
        Assert.Equal(2,events.Count(e=>e.Kind==NotificationEventKind.EngineIssued));
    }
    private sealed class TestClock : TimeProvider
    {
        public long Seconds;
        public override long TimestampFrequency=>1;
        public override long GetTimestamp()=>Seconds;
    }
    [Fact] public void SoundPreviewDoesNotPretendToBeAnEngineAlert()
    {
        Router().PreviewSound(.5);
        Assert.Equal(0,overlay.Calls); Assert.Equal(1,sound.Calls); Assert.Equal(.5,sound.Volume);
        Assert.DoesNotContain(events,e=>e.Kind==NotificationEventKind.EngineIssued);
    }
    [Fact] public void ManualTestBypassesEngineAndChannelSwitches()
    {
        Router().Test(Options);
        Assert.Equal(1,overlay.Calls); Assert.Equal(1,sound.Calls);
        Assert.Contains(events,e=>e.Kind==NotificationEventKind.TestRequested);
        Assert.DoesNotContain(events,e=>e.Kind==NotificationEventKind.EngineIssued);
    }
    [Fact] public void ZeroVolumeIsExplicitlyDiagnosedAndDoesNotBlockOverlay()
    {
        Router().Test(Options with { Volume=0 });
        Assert.Equal(1,overlay.Calls); Assert.Equal(0,sound.Calls);
        Assert.Contains(events,e=>e.Kind==NotificationEventKind.SoundRequested);
        Assert.Contains(events,e=>e.Kind==NotificationEventKind.SoundMuted);
    }
    [Theory] [InlineData(-.1)] [InlineData(1.1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void VolumeIsValidated(double volume) => Assert.Throws<ArgumentException>(() => Router().Test(Options with { Volume=volume }));

    private sealed class FakeOverlay : IOverlayChannel
    {
        public int Calls, Dismissals, Hp;
        public (double,double) Position;
        public Exception? Error, DismissError;
        public string Show(int hp,double left,double top)
        {
            Calls++; Hp=hp; Position=(left,top);
            if (Error is not null) throw Error;
            return "Fake overlay shown";
        }
        public void Dismiss() { Dismissals++; if (DismissError is not null) throw DismissError; }
    }
    private sealed class FakeSound : ISoundChannel
    {
        public int Calls, Stops; public double Volume;
        public Exception? Error, StopError;
        public Action<Exception?>? Completion;
        public void Play(double volume,Action<Exception?> completed)
        {
            Calls++; Volume=volume;
            if (Error is not null) throw Error;
            Completion=completed;
        }
        public void Finish(Exception? error) => Completion!(error);
        public void Stop() { Stops++; if (StopError is not null) throw StopError; }
    }
}
