namespace DotaHpAlert.Core;

public enum NotificationEventKind
{
    EngineIssued, TestRequested, DeliverySuppressed,
    OverlayRequested, OverlayShown, OverlayError, OverlayDismissError,
    SoundRequested, SoundStarted, SoundCompleted, SoundSkippedBusy, SoundMuted, SoundError
}
public sealed record NotificationEvent(NotificationEventKind Kind, string Message);
public sealed record NotificationOptions(double Left, double Top, double Volume)
{
    public void Validate()
    {
        if (!double.IsFinite(Left) || !double.IsFinite(Top) || Math.Abs(Left) > 32768 || Math.Abs(Top) > 32768 ||
            !double.IsFinite(Volume) || Volume < 0 || Volume > 1)
            throw new ArgumentException("Invalid notification position or volume (0–1).");
    }
}
public interface IOverlayChannel
{
    string Show(int hp, double left, double top);
    void Dismiss();
}
public interface ISoundChannel
{
    // On success, invoke completed exactly once (possibly asynchronously). On synchronous failure, throw.
    void Play(double volume, Action<Exception?> completed);
    void Stop();
}

/// <summary>Call Route, Test and Stop serially. Sound completion may arrive on another thread.</summary>
public sealed class NotificationRouter(IOverlayChannel overlay, ISoundChannel sound, Action<NotificationEvent> diagnostic)
{
    private readonly object soundGate = new();
    private object? activeSound;

    public void Route(AlertDecision decision, int hp, NotificationOptions options, bool deliveryEnabled)
    {
        if (decision.Action == AlertAction.Reset) { Stop(); return; }
        if (decision.Action is not (AlertAction.Show or AlertAction.Repeat)) return;
        Emit(NotificationEventKind.EngineIssued, $"AlertEngine: {decision.Action}, reason={decision.Reason}, HP={hp}, visual={decision.ShowVisual}, sound={decision.PlaySound}");
        if (!deliveryEnabled)
        {
            Emit(NotificationEventKind.DeliverySuppressed, "Delivery disabled: profile off, stopping or closing.");
            return;
        }
        Send(hp, options, decision.ShowVisual, decision.PlaySound);
    }
    public void Test(NotificationOptions options)
    {
        Emit(NotificationEventKind.TestRequested, "Manual test: bypass OCR, game, profile and channel switches.");
        Send(123, options, visual: true, audio: true);
    }
    public void PreviewSound(double volume)
    {
        Emit(NotificationEventKind.TestRequested,"Предпрослушивание звука без AlertEngine и оверлея.");
        Send(0,new(0,0,volume),visual:false,audio:true);
    }
    private void Send(int hp, NotificationOptions options, bool visual, bool audio)
    {
        options.Validate();
        if (visual)
        {
            Emit(NotificationEventKind.OverlayRequested, $"Show overlay: HP={hp}, requested DIP=({options.Left}, {options.Top})");
            try { Emit(NotificationEventKind.OverlayShown, overlay.Show(hp, options.Left, options.Top)); }
            catch (Exception error) { Emit(NotificationEventKind.OverlayError, error.ToString()); }
        }
        if (!audio) return;
        Emit(NotificationEventKind.SoundRequested, $"Play selected sound preset: volume={options.Volume:P0}");
        if (options.Volume == 0) { Emit(NotificationEventKind.SoundMuted, "Volume is zero."); return; }
        var ticket = new object();
        bool busy;
        lock (soundGate)
        {
            busy = activeSound is not null;
            if (!busy) activeSound = ticket;
        }
        if (busy) { Emit(NotificationEventKind.SoundSkippedBusy, "Previous alert sound is still playing; no overlap."); return; }
        try
        {
            sound.Play(options.Volume, error => Complete(ticket, error));
            Emit(NotificationEventKind.SoundStarted, "Audio backend accepted playback (physical audibility depends on Windows output/mixer).");
        }
        catch (Exception error) { Complete(ticket, error); }
    }
    private void Complete(object ticket, Exception? error)
    {
        lock (soundGate)
        {
            if (!ReferenceEquals(activeSound, ticket)) return;
            activeSound = null;
        }
        Emit(error is null ? NotificationEventKind.SoundCompleted : NotificationEventKind.SoundError,
            error?.ToString() ?? "Playback completed.");
    }
    public void Stop()
    {
        try { overlay.Dismiss(); }
        catch (Exception error) { Emit(NotificationEventKind.OverlayDismissError, error.ToString()); }
        try
        {
            sound.Stop();
            lock (soundGate) activeSound = null; // ignore completion of any earlier playback
        }
        catch (Exception error)
        {
            // Until a later successful Stop, do not start another sound over a possibly live output.
            lock (soundGate) activeSound ??= new object();
            Emit(NotificationEventKind.SoundError, error.ToString());
        }
    }
    private void Emit(NotificationEventKind kind, string message) => diagnostic(new(kind, message));
}
