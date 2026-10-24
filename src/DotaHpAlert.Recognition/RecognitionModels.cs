using DotaHpAlert.Core;
namespace DotaHpAlert.Recognition;

public enum PreprocessingMode { Grayscale, BrightText, Otsu }
public sealed record OcrOptions
{
    public PreprocessingMode Preprocessing { get; init; } = PreprocessingMode.Otsu;
    public int PageSegmentationMode { get; init; } = 7;
    public double MinimumConfidence { get; init; } = 0.65;
    public int MaxHp { get; init; } = 100_000;
    public void Validate()
    {
        if (!Enum.IsDefined(Preprocessing) || PageSegmentationMode is not (7 or 8 or 13) ||
            !double.IsFinite(MinimumConfidence) || MinimumConfidence < 0 || MinimumConfidence > 1 || MaxHp < 1)
            throw new ArgumentException("Invalid OCR options.");
    }
}
public sealed record RecognitionResult(HealthReading Reading, string RawText, TimeSpan Duration, string Diagnostic);

public static class HpTextParser
{
    // Accept only a complete current value, optionally followed by / maximum.
    public static bool TryParse(string text, int maxHp, out int hp)
    {
        hp = 0;
        if (text.Length > 32 || maxHp < 1) return false;
        var match = System.Text.RegularExpressions.Regex.Match(text.Trim(),
            @"^([0-9]{1,6})(?:\s*/\s*([0-9]{1,6}))?$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(50));
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out hp) || hp > maxHp) return false;
        return !match.Groups[2].Success ||
            (int.TryParse(match.Groups[2].Value, out var maximum) && maximum > 0 && maximum <= maxHp && hp <= maximum);
    }
}
