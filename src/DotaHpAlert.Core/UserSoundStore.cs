namespace DotaHpAlert.Core;

public static class UserSoundStore
{
    public const long MaximumBytes = 20 * 1024 * 1024;
    public static string Import(string source, string directory, Action<string> validate)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".wav" or ".mp3")) throw new ArgumentException("Поддерживаются только WAV и MP3.");
        var file = new FileInfo(source);
        if (!file.Exists || file.Length == 0 || file.Length > MaximumBytes)
            throw new IOException("Аудиофайл отсутствует, пуст или больше 20 МБ.");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);
        try { File.Copy(source,target); validate(target); return target; }
        catch { if (File.Exists(target)) File.Delete(target); throw; }
    }
    public static T OpenWithFallback<T>(string? custom, Func<string,T> open, string standard, Action<string> report)
    {
        if (!string.IsNullOrWhiteSpace(custom))
        {
            try { return open(custom); }
            catch (Exception error) { report($"Пользовательский звук недоступен: {error.Message}. Возвращён стандартный сигнал."); }
        }
        return open(standard);
    }
}
