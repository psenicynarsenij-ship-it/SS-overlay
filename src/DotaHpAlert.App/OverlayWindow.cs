using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace DotaHpAlert.App;

internal sealed class OverlayWindow : Window
{
    private bool closed;
    private readonly TextBlock label = new() { Foreground = Brushes.White, FontSize = 26, Margin = new Thickness(18) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    public OverlayWindow()
    {
        Width = 330; Height = 85; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
        ShowInTaskbar = false; ShowActivated = false; Focusable = false; IsHitTestVisible = false;
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(220, 145, 15, 15)),
            CornerRadius = new CornerRadius(10), Child = label };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(hwnd, -20).ToInt64();
            SetWindowLongPtr(hwnd, -20, new IntPtr(style | 0x20 | 0x08000000 | 0x80));
            SetWindowDisplayAffinity(hwnd, 0x11); // transparent, no-activate, toolwindow
        };
        timer.Tick += (_, _) => Dismiss();
        Closed += (_, _) => { closed = true; timer.Stop(); };
    }
    public void Notify(int hp, double left, double top)
    {
        if (closed) return;
        label.Text = $"Низкое HP: {hp}";
        Left = left; Top = top; Show();
        timer.Stop(); timer.Start();
    }
    public void Dismiss() { timer.Stop(); if (!closed) Hide(); }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
