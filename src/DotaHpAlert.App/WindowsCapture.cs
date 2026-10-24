using DotaHpAlert.Recognition;
using System.ComponentModel;
using System.Runtime.InteropServices;
using DotaHpAlert.Core;
namespace DotaHpAlert.App;

/// <summary>Captures visible client-area pixels only; never reads process memory.</summary>
internal static class WindowsCapture
{
    public static ImageFrame Capture(string title, CaptureRegion region)
    {
        region.Validate();
        var window = FindWindow(null, title);
        if (window == IntPtr.Zero) throw new InvalidOperationException($"Window '{title}' was not found.");
        if (IsIconic(window) || !IsWindowVisible(window)) throw new InvalidOperationException("Game window is minimized or hidden.");
        if (GetForegroundWindow() != window) throw new InvalidOperationException("Capture paused: bring the game window to foreground.");
        if (!GetClientRect(window, out var client)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if ((long)region.X + region.Width > client.Right || (long)region.Y + region.Height > client.Bottom)
            throw new InvalidOperationException("HP region is outside the game client. Adjust pixel coordinates.");
        var point = new Point { X = region.X, Y = region.Y };
        if (!ClientToScreen(window, ref point)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = new BitmapInfo { Size = 40, Width = region.Width, Height = -region.Height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(screen, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            previous = SelectObject(memory, bitmap);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!BitBlt(memory, 0, 0, region.Width, region.Height, screen, point.X, point.Y, 0x00CC0020))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (GetForegroundWindow() != window || IsIconic(window))
                throw new InvalidOperationException("Capture discarded: game lost foreground during capture.");
            var pixels = new byte[region.Width * region.Height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (var i = 0; i < pixels.Length; i += 4)
            {
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
                pixels[i + 3] = 255;
            }
            return new ImageFrame(region.Width, region.Height, pixels);
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1) && memory != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint operation);
}
