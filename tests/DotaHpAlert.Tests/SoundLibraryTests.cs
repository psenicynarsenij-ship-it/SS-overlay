using DotaHpAlert.Core;
using Xunit;
namespace DotaHpAlert.Tests;
public sealed class SoundLibraryTests
{
    [Fact] public void LibraryContainsExactlyTheSixRequestedFiles()
    {
        Assert.Equal(new[]{"hp-alert.wav","short-loud-chime.mp3","error-notification.mp3","steam-.mp3","delivered-message-sound.mp3","steam-achievement.mp3"},SoundLibrary.Presets.Select(p=>p.FileName));
        Assert.Equal(6,SoundLibrary.Presets.Select(p=>p.Id).Distinct().Count());
    }
    public static IEnumerable<object[]> Ids()=>SoundLibrary.Presets.Select(p=>new object[]{p.Id});
    [Theory][MemberData(nameof(Ids))] public void PresetSelectionAndVolumeSurviveRestart(string id)
    {
        var path=Path.GetTempFileName();
        try { SettingsStore.Save(path,new() { SoundPresetId=id,SoundVolume=.45 }); var saved=SettingsStore.Load(path); Assert.Equal(id,saved.SoundPresetId);Assert.Equal(.45,saved.SoundVolume); }
        finally {File.Delete(path);}
    }
    [Theory][InlineData("../../outside.mp3")][InlineData("unknown")][InlineData(null)]
    public void UnknownIdsNeverBecomeExternalFilePaths(string? id)
    {
        Assert.Equal("standard",SoundLibrary.Find(id).Id);
        Assert.Equal(Path.Combine("root","sounds","hp-alert.wav"),SoundLibrary.PathFor(id,"root"));
    }
    [Fact] public void LegacyImportedPathIsIgnoredWithoutLosingOtherSettings()
    {
        var path=Path.GetTempFileName();
        try {File.WriteAllText(path,"{\"SoundFilePath\":\"external.mp3\",\"SoundVolume\":0.3}"); var result=SettingsStore.Load(path);Assert.Equal("standard",result.SoundPresetId);Assert.Equal(.3,result.SoundVolume);}
        finally {File.Delete(path);}
    }
    [Theory][MemberData(nameof(Ids))] public void SuccessfulPresetUsesItsOwnPackagedFile(string id)
    {
        var result=SoundLibrary.OpenWithFallback(id,"root",p=>p,_=>throw new Exception());
        Assert.Equal(SoundLibrary.PathFor(id,"root"),result);
    }
    [Theory][InlineData("chime")][InlineData("error")][InlineData("steam")][InlineData("message")][InlineData("achievement")]
    public void AnyMissingOrCorruptPresetFallsBack(string id)
    {
        var calls=new List<string>(); string? warning=null;
        var result=SoundLibrary.OpenWithFallback(id,"root",p=>{calls.Add(p);return p.EndsWith("hp-alert.wav")?"default":throw new InvalidDataException("broken");},m=>warning=m);
        Assert.Equal("default",result);Assert.Equal(2,calls.Count);Assert.NotNull(warning);
    }
    [Fact] public void BrokenStandardDoesNotRetryForever()=>Assert.Throws<IOException>(()=>SoundLibrary.OpenWithFallback<string>("standard","root",_=>throw new IOException(),_=>{}));
}
