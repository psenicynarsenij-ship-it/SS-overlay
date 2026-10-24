namespace DotaHpAlert.Core;

public sealed record SoundPreset(string Id, string Name, string FileName);
public static class SoundLibrary
{
    public const string StandardId = "standard";
    public static IReadOnlyList<SoundPreset> Presets { get; } = Array.AsReadOnly(new[]
    {
        new SoundPreset(StandardId,"Стандартный сигнал","hp-alert.wav"),
        new SoundPreset("chime","Звонкий сигнал","short-loud-chime.mp3"),
        new SoundPreset("error","Уведомление об ошибке","error-notification.mp3"),
        new SoundPreset("steam","Steam — уведомление","steam-.mp3"),
        new SoundPreset("message","Сообщение доставлено","delivered-message-sound.mp3"),
        new SoundPreset("achievement","Достижение","steam-achievement.mp3")
    });
    public static SoundPreset Find(string? id) => Presets.FirstOrDefault(p=>p.Id==id) ?? Presets[0];
    public static string PathFor(string? id,string root) => Path.Combine(root,"sounds",Find(id).FileName);
    public static T OpenWithFallback<T>(string? id,string root,Func<string,T> open,Action<string> report)
    {
        var preset=Find(id);
        try { return open(PathFor(preset.Id,root)); }
        catch(Exception error) when(preset.Id!=StandardId)
        {
            report($"Не удалось воспроизвести «{preset.Name}»: {error.Message}. Возвращён стандартный сигнал.");
            return open(PathFor(StandardId,root));
        }
    }
}
