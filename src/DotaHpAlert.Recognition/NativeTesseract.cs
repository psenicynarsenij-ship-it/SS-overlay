using System.Reflection;
using System.Runtime.InteropServices;
namespace DotaHpAlert.Recognition;

// Minimal binding to the stable Tesseract C API; one instance per serial worker.
internal static class NativeTesseract
{
    private const string Library = "DotaHpTesseract";
    private static readonly Lazy<IntPtr> Module = new(LoadModule);
    static NativeTesseract() => NativeLibrary.SetDllImportResolver(typeof(NativeTesseract).Assembly, Resolve);
    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Library) return IntPtr.Zero;
        return Module.Value;
    }
    private static IntPtr LoadModule()
    {
        // Keep small ROI OCR from spawning an OpenMP team per frame; respect explicit user overrides.
        if (Environment.GetEnvironmentVariable("OMP_THREAD_LIMIT") is null)
            Environment.SetEnvironmentVariable("OMP_THREAD_LIMIT", "1");
        if (OperatingSystem.IsWindows())
        {
            if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("Windows x64 is required.");
            NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "x64", "leptonica-1.82.0.dll"));
            return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "x64", "tesseract50.dll"));
        }
        return NativeLibrary.Load("libtesseract.so.5");
    }
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr TessBaseAPICreate();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void TessBaseAPIDelete(IntPtr handle);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int TessBaseAPIInit3(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string language);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void TessBaseAPISetPageSegMode(IntPtr handle, int mode);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int TessBaseAPISetVariable(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void TessBaseAPISetImage(IntPtr handle, byte[] pixels, int width, int height, int bytesPerPixel, int bytesPerLine);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr TessBaseAPIGetUTF8Text(IntPtr handle);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int TessBaseAPIMeanTextConf(IntPtr handle);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void TessDeleteText(IntPtr text);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void TessBaseAPIClear(IntPtr handle);
}
