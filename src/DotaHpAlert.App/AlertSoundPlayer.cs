using System.IO;
using DotaHpAlert.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DotaHpAlert.App;

/// <summary>UI-thread-owned playback. Each request has one output; Stop disposes before another starts.</summary>
internal sealed class AlertSoundPlayer : ISoundChannel, IDisposable
{
    private WaveOutEvent? output;
    private WaveFileReader? reader;
    private EventHandler<StoppedEventArgs>? stopped;

    public void Play(double volume, Action<Exception?> completed)
    {
        Stop();
        try
        {
            reader = new WaveFileReader(Path.Combine(AppContext.BaseDirectory, "sounds", "hp-alert.wav"));
            var samples = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = (float)volume };
            var device = new WaveOutEvent { DesiredLatency = 80 };
            output = device;
            stopped = (_, args) =>
            {
                // WaveOutEvent captures the WPF SynchronizationContext. Ignore stale completions after Stop.
                if (!ReferenceEquals(output, device)) return;
                var failure = args.Exception;
                try { Stop(); }
                catch (Exception error) { failure ??= error; }
                completed(failure);
            };
            device.PlaybackStopped += stopped;
            device.Init(samples.ToWaveProvider16());
            device.Play();
        }
        catch { Stop(); throw; }
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
