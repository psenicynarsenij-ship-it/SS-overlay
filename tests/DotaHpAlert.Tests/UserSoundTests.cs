using DotaHpAlert.Core;
using Xunit;
namespace DotaHpAlert.Tests;
public sealed class UserSoundTests
{
    [Fact] public void ImportCopiesAndSurvivesDeletionOfOriginal()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        try
        {
            var source=Path.Combine(dir,"sound.MP3"); File.WriteAllText(source,"audio");
            var copy=UserSoundStore.Import(source,Path.Combine(dir,"owned"),p=>Assert.Equal("audio",File.ReadAllText(p)));
            File.Delete(source); Assert.Equal("audio",File.ReadAllText(copy));
            Assert.EndsWith(".mp3",copy);
        }
        finally { Directory.Delete(dir,true); }
    }
    [Fact] public void FailedValidationRemovesOnlyNewCopy()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        try
        {
            var source=Path.Combine(dir,"bad.wav"); File.WriteAllText(source,"broken");
            Assert.Throws<InvalidDataException>(()=>UserSoundStore.Import(source,Path.Combine(dir,"owned"),_=>throw new InvalidDataException()));
            Assert.Empty(Directory.GetFiles(Path.Combine(dir,"owned"))); Assert.True(File.Exists(source));
        }
        finally { Directory.Delete(dir,true); }
    }
    [Fact] public void UnsupportedInputIsRejected() => Assert.Throws<ArgumentException>(()=>UserSoundStore.Import("x.ogg","unused",_=>{}));
    [Theory] [InlineData("missing.mp3")] [InlineData("corrupt.wav")]
    public void CustomFailureFallsBackAndReports(string path)
    {
        string? warning=null;
        var result=UserSoundStore.OpenWithFallback(path,p=>p=="standard"?"OK":throw new IOException("invalid"),"standard",m=>warning=m);
        Assert.Equal("OK",result); Assert.Contains("стандартный",warning);
    }
    [Fact] public void HealthyCustomDoesNotOpenFallback() => Assert.Equal("custom",UserSoundStore.OpenWithFallback("custom",p=>p,"standard",_=>throw new Exception()));
    [Fact] public void NewSettingsRoundTripAndOldDefaults()
    {
        var file=Path.GetTempFileName();
        try
        {
            File.WriteAllText(file,"{}"); var old=SettingsStore.Load(file);
            Assert.Null(old.SoundFilePath); Assert.True(old.OverlayAnimation); Assert.Equal("Custom",old.OverlayPosition);
            var updated=old with { SoundFilePath="owned.mp3",OverlayPosition="TopCenter",OverlayAnimation=false,SoundVolume=.4 };
            SettingsStore.Save(file,updated); Assert.Equal(updated,SettingsStore.Load(file));
        }
        finally { File.Delete(file); }
    }
}
