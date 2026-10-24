namespace DotaHpAlert.Core;

/// <summary>Serialize calls from a single consumer. Uses monotonic time for cooldowns.</summary>
public sealed class AlertEngine
{
    private readonly TimeProvider time;
    private long? lastAlertTimestamp;
    public AlertState State { get; private set; }
    public AlertSettings Settings { get; private set; }

    public AlertEngine(AlertSettings settings, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        Settings = settings;
        time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Apply settings immediately, including resetting when the profile is disabled.</summary>
    public AlertDecision UpdateSettings(AlertSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        Settings = settings;
        return !settings.InvokerEnabled
            ? Reset(AlertReason.ProfileDisabled)
            : Decision(AlertAction.None, AlertReason.SettingsUpdated);
    }

    public AlertDecision Evaluate(HealthReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (!Settings.InvokerEnabled) return Reset(AlertReason.ProfileDisabled);
        if (reading.IsAlive == false) return Reset(AlertReason.HeroDead);
        if (!reading.IsValid || reading.CurrentHp < 0 ||
            !double.IsFinite(reading.Confidence) || reading.Confidence < 0 || reading.Confidence > 1)
            return Decision(AlertAction.None, AlertReason.InvalidReading);
        if (reading.CurrentHp >= Settings.HpThreshold + Settings.Hysteresis)
            return Reset(AlertReason.HealthRecovered);
        if (State == AlertState.Idle)
        {
            if (reading.CurrentHp >= Settings.HpThreshold)
                return Decision(AlertAction.None, reading.CurrentHp == Settings.HpThreshold
                    ? AlertReason.AboveThreshold : AlertReason.HysteresisBand);
            State = AlertState.Active;
            lastAlertTimestamp = time.GetTimestamp();
            return Decision(AlertAction.Show, AlertReason.ThresholdCrossed);
        }
        if (time.GetElapsedTime(lastAlertTimestamp!.Value) < Settings.RepeatInterval)
            return Decision(AlertAction.None, AlertReason.Cooldown);
        lastAlertTimestamp = time.GetTimestamp();
        return Decision(AlertAction.Repeat, AlertReason.RepeatDue);
    }

    private AlertDecision Reset(AlertReason reason)
    {
        var action = State == AlertState.Active ? AlertAction.Reset : AlertAction.None;
        State = AlertState.Idle;
        lastAlertTimestamp = null;
        return Decision(action, reason);
    }

    private AlertDecision Decision(AlertAction action, AlertReason reason)
    {
        var notify = action is AlertAction.Show or AlertAction.Repeat;
        return new(action, State, reason, notify && Settings.SoundEnabled, notify && Settings.VisualEnabled);
    }
}
