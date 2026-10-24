namespace DotaHpAlert.Core;

public sealed record HealthReading(
    int CurrentHp,
    DateTimeOffset MeasuredAt,
    double Confidence,
    bool IsValid,
    bool? IsAlive = null);

public sealed record AlertSettings
{
    public int HpThreshold { get; init; } = 350;
    public TimeSpan RepeatInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int Hysteresis { get; init; } = 25;
    public bool InvokerEnabled { get; init; }
    public bool SoundEnabled { get; init; } = true;
    public bool VisualEnabled { get; init; } = true;

    internal void Validate()
    {
        if (HpThreshold <= 0) throw new ArgumentOutOfRangeException(nameof(HpThreshold));
        if (Hysteresis < 0 || (long)HpThreshold + Hysteresis > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(Hysteresis));
        if (RepeatInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(RepeatInterval));
    }
}

public enum AlertAction { None, Show, Repeat, Reset }
public enum AlertState { Idle, Active }
public enum AlertReason
{
    SettingsUpdated, ProfileDisabled, HeroDead, InvalidReading, HealthRecovered,
    AboveThreshold, HysteresisBand, ThresholdCrossed, Cooldown, RepeatDue
}

public sealed record AlertDecision(
    AlertAction Action, AlertState State, AlertReason Reason,
    bool PlaySound = false, bool ShowVisual = false);
