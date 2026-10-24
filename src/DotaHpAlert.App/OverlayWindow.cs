using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DotaHpAlert.Core;

namespace DotaHpAlert.App;

internal sealed class OverlayWindow : Window, IOverlayChannel
{
    private const long ExtendedStyles = 0x20 | 0x08000000 | 0x80; // transparent, no-activate, toolwindow
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
            ApplyStyles(hwnd);
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessages);
        };
        timer.Tick += (_, _) => Dismiss();
        Closed += (_, _) => { closed = true; timer.Stop(); };
    }
    public string Show(int hp, double left, double top)
    {
        Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(closed, this);
        label.Text = $"Низкое HP: {hp}";
        Left = left; Top = top;
        var foregroundBefore = GetForegroundWindow();
        Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        ApplyStyles(hwnd);
        if (!GetWindowRect(hwnd, out var rect)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var relocated = MonitorFromRect(ref rect, 0) == IntPtr.Zero;
        if (relocated)
        {
            // An old monitor layout must not make a successful notification invisible off-screen.
            Left = SystemParameters.WorkArea.Left + 20;
            Top = SystemParameters.WorkArea.Top + 20;
        }
        // Reassert native topmost on every notification; do not activate or change keyboard focus.
        if (!SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040 | 0x0200))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        UpdateLayout();
        if (!IsWindowVisible(hwnd)) throw new InvalidOperationException("Overlay HWND is not visible after Show.");
        timer.Stop(); timer.Start();
        GetWindowRect(hwnd, out rect);
        return $"Overlay HWND visible; pixels=({rect.Left},{rect.Top})–({rect.Right},{rect.Bottom}), topmost=true, " +
            $"foreground before=0x{foregroundBefore:X}, after=0x{GetForegroundWindow():X}, relocated={relocated}. " +
            "This confirms HWND visibility, not visibility over exclusive fullscreen.";
    }
    private static void ApplyStyles(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, -20).ToInt64();
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(hwnd, -20, new IntPtr(style | ExtendedStyles));
        if (previous == IntPtr.Zero && Marshal.GetLastWin32Error() != 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if ((GetWindowLongPtr(hwnd, -20).ToInt64() & ExtendedStyles) != ExtendedStyles)
            throw new InvalidOperationException("Overlay no-activate/click-through styles could not be applied.");
    }
    private static IntPtr WindowMessages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new IntPtr(3); } // WM_MOUSEACTIVATE: MA_NOACTIVATE
        if (message == 0x0084) { handled = true; return new IntPtr(-1); } // WM_NCHITTEST: HTTRANSPARENT
        return IntPtr.Zero;
    }
    public void Dismiss() { timer.Stop(); if (!closed) Hide(); }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
