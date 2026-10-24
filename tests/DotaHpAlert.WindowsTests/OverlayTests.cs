using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using DotaHpAlert.App;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace DotaHpAlert.WindowsTests;

public sealed class OverlayTests
{
    [Fact] public void OverlayHasNativeClickThroughNoActivateAndTopmostStyles() => OnSta(() =>
    {
        var overlay = new OverlayWindow();
        try
        {
            var foreground = GetForegroundWindow();
            var diagnostic = overlay.Show(59,30,30);
            var hwnd = new WindowInteropHelper(overlay).Handle;
            Assert.True(IsWindowVisible(hwnd));
            Assert.False(overlay.IsActive);
            Assert.Equal(foreground, GetForegroundWindow());
            Assert.NotEqual(hwnd, GetForegroundWindow());
            var styles = GetWindowLongPtr(hwnd,-20).ToInt64();
            Assert.Equal(0x20L | 0x08000000L | 0x80L | 0x8L, styles & (0x20L | 0x08000000L | 0x80L | 0x8L));
            Assert.Equal(new IntPtr(-1),SendMessage(hwnd,0x0084,IntPtr.Zero,IntPtr.Zero));
            Assert.Equal(new IntPtr(3),SendMessage(hwnd,0x0021,IntPtr.Zero,IntPtr.Zero));
            Assert.Contains("Overlay HWND visible", diagnostic);
            overlay.Dismiss(); Assert.False(IsWindowVisible(hwnd));
            overlay.Show(59,30,30); Assert.True(IsWindowVisible(hwnd));
        }
        finally { overlay.Close(); }
    });
    [Fact] public void OffscreenOverlayIsRelocatedAndClosedOverlayReportsError() => OnSta(() =>
    {
        var overlay = new OverlayWindow();
        try
        {
            Assert.Contains("relocated=True",overlay.Show(59,32000,32000));
            var hwnd = new WindowInteropHelper(overlay).Handle;
            Assert.True(GetWindowRect(hwnd,out var rect));
            Assert.NotEqual(IntPtr.Zero, MonitorFromRect(ref rect,0));
        }
        finally { overlay.Close(); }
        Assert.Throws<ObjectDisposedException>(()=>overlay.Show(59,30,30));
        overlay.Dismiss(); // late cleanup must be harmless
    });
    [Fact] public void PackagedWavDecodesAndSupportsSoftwareVolume()
    {
        var path = Path.Combine(AppContext.BaseDirectory,"sounds","hp-alert.wav");
        using var reader = new WaveFileReader(path);
        Assert.Equal(44100,reader.WaveFormat.SampleRate);
        Assert.Equal(1,reader.WaveFormat.Channels);
        Assert.InRange(reader.TotalTime.TotalSeconds,.44,.46);
        var samples = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume=1 };
        var full = new float[20000]; var count = samples.Read(full,0,full.Length);
        reader.Position=0;
        var quiet = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume=.25f };
        var reduced = new float[20000]; Assert.Equal(count,quiet.Read(reduced,0,reduced.Length));
        var peak = full.Max(Math.Abs); var quietPeak = reduced.Max(Math.Abs);
        Assert.InRange(peak,.3f,.4f); Assert.InRange(quietPeak/peak,.2499f,.2501f);
    }
    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch(Exception e) { error=e; } }) { IsBackground=true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)),"WPF test timed out.");
        if(error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SendMessageW")] private static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Rect rect,uint flags);
}
