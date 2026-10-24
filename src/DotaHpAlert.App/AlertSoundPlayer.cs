using System.IO;
using DotaHpAlert.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DotaHpAlert.App;

/// <summary>UI-thread-owned playback. Each request has one output; Stop disposes before another starts.</summary>
internal sealed class AlertSoundPlayer : ISoundChannel, IDisposable
{
    private readonly Func<IWavePlayer> outputFactory;
    public AlertSoundPlayer(Func<IWavePlayer>? outputFactory=null) =>
        this.outputFactory=outputFactory ?? (()=>new WaveOutEvent { DesiredLatency=80 });
    private IWavePlayer? output;
    private WaveStream? reader;
    public string PresetId { get; set; } = SoundLibrary.StandardId;
    public Action<string>? Diagnostic { get; set; }
    public static WaveStream OpenAudio(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".wav" => new WaveFileReader(path),
        ".mp3" => new Mp3FileReaderBase(path, format => new AcmMp3FrameDecompressor(format)),
        _ => throw new InvalidDataException("Поддерживаются WAV и MP3.")
    };
    public static void ValidateAudio(string path)
    {
        using var audio = OpenAudio(path);
        if (audio.TotalTime <= TimeSpan.Zero || audio.TotalTime > TimeSpan.FromSeconds(60))
            throw new InvalidDataException("Длительность звука должна быть от 0 до 60 секунд.");
        if(audio.WaveFormat.Channels is <1 or >2 || audio.WaveFormat.SampleRate is <8000 or >192000)
            throw new InvalidDataException("Поддерживается mono/stereo, 8–192 кГц.");
        var samples=audio.ToSampleProvider();
        var buffer = new float[4096]; long count = 0;
        while (true) { var read = samples.Read(buffer,0,buffer.Length); if(read==0) break; count+=read; }
        if(count==0) throw new InvalidDataException("Нет аудиоданных.");
    }
    private EventHandler<StoppedEventArgs>? stopped;

    public void Play(double volume, Action<Exception?> completed)
    {
        Stop();
        var requested=SoundLibrary.Find(PresetId).Id;
        var actual=requested;
        try
        {
            reader = SoundLibrary.OpenWithFallback(PresetId, AppContext.BaseDirectory, path =>
            {
                ValidateAudio(path);
                return OpenAudio(path);
            }, message =>
            {
                actual=SoundLibrary.StandardId;
                PresetId = SoundLibrary.StandardId;
                Diagnostic?.Invoke(message);
            });
            var samples = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = (float)volume };
            var device = outputFactory();
            output = device;
            stopped = (_, args) =>
            {
                // WaveOutEvent captures the WPF SynchronizationContext. Ignore stale completions after Stop.
                if (!ReferenceEquals(output, device)) return;
                var failure = args.Exception;
                try { Stop(); }
                catch (Exception error) { failure ??= error; }
                if(failure is not null && actual!=SoundLibrary.StandardId)
                {
                    PresetId=SoundLibrary.StandardId;
                    Diagnostic?.Invoke($"Ошибка проигрывания: {failure.Message}. Стандартный сигнал.");
                    try { Play(volume,completed); } catch(Exception error) { completed(error); }
                }
                else completed(failure);
            };
            device.PlaybackStopped += stopped;
            device.Init(samples.ToWaveProvider16());
            device.Play();
        }
        catch(Exception error)
        {
            Stop();
            if(actual==SoundLibrary.StandardId) throw;
            PresetId=SoundLibrary.StandardId;
            Diagnostic?.Invoke($"Ошибка проигрывания: {error.Message}. Стандартный сигнал.");
            Play(volume,completed);
        }
    }
    public void Stop()
    {
        var device = output;
        if (device is null) { reader?.Dispose(); reader = null; return; }
        device.PlaybackStopped -= stopped;
        Exception? failure = null;
        try { device.Stop(); }
        catch (Exception error) { failure = error; }
        try
        {
            device.Dispose();
            output = null; stopped = null;
            reader?.Dispose(); reader = null;
        }
        catch (Exception error)
        {
            // Retain ownership so a subsequent Stop/Dispose can retry releasing the output.
            failure = failure is null ? error : new AggregateException(failure, error);
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    public void Dispose() => Stop();
}
