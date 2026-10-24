using DotaHpAlert.Core;
using Xunit;
namespace DotaHpAlert.Tests;

public sealed class SettingsTests
{
    [Fact] public void MissingFileReturnsDefaults() =>
        Assert.Equal(new ApplicationSettings(), SettingsStore.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".json")));
    [Fact] public void SettingsRoundTripAndReplacementAreAtomic()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var settings = new ApplicationSettings { Alerts = new() { InvokerEnabled = true, HpThreshold = 400 }, Region = new(1,2,100,30), OverlayLeft = -500 };
            SettingsStore.Save(path, settings); Assert.Equal(settings, SettingsStore.Load(path));
            settings = settings with { CaptureIntervalMs = 200 };
            SettingsStore.Save(path, settings); Assert.Equal(settings, SettingsStore.Load(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory] [InlineData("null")] [InlineData("{bad json")]
    [InlineData("{\"Region\":null}")] [InlineData("{\"Alerts\":null}")]
    [InlineData("{\"CaptureIntervalMs\":1}")]
    public void CorruptOrInvalidSettingsAreNotSilentlyOverwritten(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,json);
            Assert.ThrowsAny<Exception>(() => SettingsStore.Load(path));
            Assert.Equal(json,File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
    [Fact] public void InvalidSettingsCannotBeSaved()
    {
        Assert.Throws<ArgumentException>(() => new ApplicationSettings { MinimumConfidence = double.PositiveInfinity }.Validate());
        Assert.Throws<ArgumentException>(() => new ApplicationSettings { OverlayTop = double.NaN }.Validate());
        Assert.Throws<ArgumentException>(() => new CaptureRegion(-1).Validate());
    }
}
