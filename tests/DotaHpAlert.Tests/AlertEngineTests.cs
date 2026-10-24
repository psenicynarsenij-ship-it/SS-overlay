using DotaHpAlert.Core;
using Xunit;

namespace DotaHpAlert.Tests;

public sealed class AlertEngineTests
{
    private readonly ManualClock clock = new();
    private AlertEngine Create(bool enabled = true) => new(new() { InvokerEnabled = enabled }, clock);
    private HealthReading Reading(int hp = 320, bool valid = true, bool? alive = true) =>
        new(hp, clock.GetUtcNow(), 0.99, valid, alive);

    [Fact] public void DefaultsMatchSpecification()
    {
        var s = new AlertSettings();
        Assert.Equal(350, s.HpThreshold);
        Assert.Equal(TimeSpan.FromSeconds(5), s.RepeatInterval);
        Assert.Equal(25, s.Hysteresis);
        Assert.False(s.InvokerEnabled);
        Assert.True(s.SoundEnabled);
        Assert.True(s.VisualEnabled);
    }
    [Fact] public void FirstCrossingShowsAlert()
    {
        var e = Create();
        Assert.Equal(AlertAction.None, e.Evaluate(Reading(800)).Action);
        var result = e.Evaluate(Reading(349));
        Assert.Equal(AlertAction.Show, result.Action);
        Assert.Equal(AlertState.Active, result.State);
        Assert.True(result.PlaySound);
        Assert.True(result.ShowVisual);
    }
    [Fact] public void ExactThresholdDoesNotActivate() =>
        Assert.Equal(AlertAction.None, Create().Evaluate(Reading(350)).Action);

    [Fact] public void RepeatsAtFiveSecondsAndNotEarlier()
    {
        var e = Create();
        e.Evaluate(Reading());
        clock.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.Equal(AlertAction.None, e.Evaluate(Reading()).Action);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(AlertAction.Repeat, e.Evaluate(Reading()).Action);
        Assert.Equal(AlertAction.None, e.Evaluate(Reading()).Action);
    }
    [Fact] public void FrequentMeasurementsDoNotDuplicateAlerts()
    {
        var e = Create();
        e.Evaluate(Reading());
        for (var i = 0; i < 1000; i++)
            Assert.Equal(AlertAction.None, e.Evaluate(Reading()).Action);
    }
    [Theory] [InlineData(375)] [InlineData(400)]
    public void RecoveryResets(int hp)
    {
        var e = Create();
        e.Evaluate(Reading());
        Assert.Equal(AlertAction.Reset, e.Evaluate(Reading(hp)).Action);
        Assert.Equal(AlertState.Idle, e.State);
    }
    [Theory] [InlineData(350)] [InlineData(374)]
    public void HysteresisPreservesPreviousState(int hp)
    {
        var e = Create();
        Assert.Equal(AlertAction.None, e.Evaluate(Reading(hp)).Action);
        Assert.Equal(AlertState.Idle, e.State);
        e.Evaluate(Reading());
        Assert.Equal(AlertState.Active, e.Evaluate(Reading(hp)).State);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(AlertAction.Repeat, e.Evaluate(Reading(hp)).Action);
    }
    [Fact] public void DisabledProfileDoesNotAlert() =>
        Assert.Equal(AlertAction.None, Create(false).Evaluate(Reading()).Action);
    [Fact] public void DisablingProfileResetsImmediately()
    {
        var e = Create();
        e.Evaluate(Reading());
        Assert.Equal(AlertAction.Reset, e.UpdateSettings(e.Settings with { InvokerEnabled = false }).Action);
        Assert.Equal(AlertState.Idle, e.State);
        Assert.Equal(AlertAction.None, e.Evaluate(Reading()).Action);
        e.UpdateSettings(e.Settings with { InvokerEnabled = true });
        Assert.Equal(AlertAction.Show, e.Evaluate(Reading()).Action);
    }
    [Fact] public void DeathSuppressesAndResetsAlert()
    {
        var e = Create();
        Assert.Equal(AlertAction.None, e.Evaluate(Reading(alive: false)).Action);
        e.Evaluate(Reading());
        Assert.Equal(AlertAction.Reset, e.Evaluate(Reading(alive: false)).Action);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(AlertAction.None, e.Evaluate(Reading(alive: false)).Action);
    }
    [Fact] public void UnknownAliveStateAllowsValidReading() =>
        Assert.Equal(AlertAction.Show, Create().Evaluate(Reading(alive: null)).Action);
    [Fact] public void InvalidReadingDoesNotActivateRepeatOrReset()
    {
        var e = Create();
        Assert.Equal(AlertAction.None, e.Evaluate(Reading(valid: false)).Action);
        e.Evaluate(Reading());
        clock.Advance(TimeSpan.FromSeconds(5));
        var result = e.Evaluate(Reading(800, valid: false));
        Assert.Equal(AlertAction.None, result.Action);
        Assert.Equal(AlertState.Active, result.State);
        Assert.Equal(AlertAction.Repeat, e.Evaluate(Reading()).Action);
    }
    [Fact] public void CanReactivateImmediatelyAfterRecovery()
    {
        var e = Create();
        e.Evaluate(Reading());
        e.Evaluate(Reading(400));
        Assert.Equal(AlertAction.Show, e.Evaluate(Reading(349)).Action);
    }
    [Fact] public void MeasurementTimestampDoesNotControlCooldown()
    {
        var e = Create();
        e.Evaluate(Reading());
        Assert.Equal(AlertAction.None, e.Evaluate(Reading() with { MeasuredAt = DateTimeOffset.MaxValue }).Action);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(AlertAction.Repeat, e.Evaluate(Reading() with { MeasuredAt = DateTimeOffset.MinValue }).Action);
    }
    [Fact] public void OutputPreferencesAreRespected()
    {
        var e = new AlertEngine(new() { InvokerEnabled = true, SoundEnabled = false, VisualEnabled = false }, clock);
        var result = e.Evaluate(Reading());
        Assert.Equal(AlertAction.Show, result.Action);
        Assert.False(result.PlaySound);
        Assert.False(result.ShowVisual);
    }
    [Theory] [InlineData(-1)] [InlineData(2)] [InlineData(double.NaN)]
    public void MalformedConfidenceIsInvalid(double confidence) =>
        Assert.Equal(AlertReason.InvalidReading, Create().Evaluate(Reading() with { Confidence = confidence }).Reason);
    [Fact] public void NegativeHpIsInvalid() =>
        Assert.Equal(AlertReason.InvalidReading, Create().Evaluate(Reading(-1)).Reason);
    [Fact] public void InvalidSettingsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlertEngine(new() { RepeatInterval = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlertEngine(new() { Hysteresis = -1 }));
    }
    private sealed class ManualClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(ticks);
        public void Advance(TimeSpan elapsed) => ticks += elapsed.Ticks;
    }
}
