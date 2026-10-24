using System.Text.Json;
namespace DotaHpAlert.Core;

public sealed record CaptureRegion(int X = 855, int Y = 1018, int Width = 140, int Height = 25)
{
    public void Validate()
    {
        if (X < 0 || Y < 0 || X > 16384 || Y > 16384 || Width < 2 || Height < 2 || Width > 2048 || Height > 512)
            throw new ArgumentException("Invalid HP region. Use client-area pixel coordinates, width 2–2048, height 2–512.");
    }
}
public sealed record ApplicationSettings
{
    public AlertSettings Alerts { get; init; } = new();
    public CaptureRegion Region { get; init; } = new();
    public string WindowTitle { get; init; } = "Dota 2";
    public int CaptureIntervalMs { get; init; } = 500;
    public double MinimumConfidence { get; init; } = 0.65;
    public double SoundVolume { get; init; } = 0.7;
    public double OverlayLeft { get; init; } = 30;
    public double OverlayTop { get; init; } = 30;
    public void Validate()
    {
        if (Alerts is null || Region is null) throw new ArgumentException("Missing settings section.");
        Alerts.Validate(); Region.Validate();
        new NotificationOptions(OverlayLeft, OverlayTop, SoundVolume).Validate();
        if (string.IsNullOrWhiteSpace(WindowTitle) || WindowTitle.Length > 256 || CaptureIntervalMs < 100 || CaptureIntervalMs > 10000 ||
            !double.IsFinite(MinimumConfidence) || MinimumConfidence < 0 || MinimumConfidence > 1 ||
            !double.IsFinite(OverlayLeft) || !double.IsFinite(OverlayTop) || Math.Abs(OverlayLeft) > 32768 || Math.Abs(OverlayTop) > 32768)
            throw new ArgumentException("Invalid capture interval, confidence, window title or overlay position.");
    }
}
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static ApplicationSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Settings file is too large.");
        var settings = JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Empty settings file.");
        settings.Validate(); return settings;
    }
    public static void Save(string path, ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings); settings.Validate();
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
