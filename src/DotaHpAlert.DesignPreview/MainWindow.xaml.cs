using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DotaHpAlert.DesignPreview;
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Optional CI export renders the actual WPF visual tree. Normal double-click opens the tabs.
        var args = Environment.GetCommandLineArgs();
        if (args.Length == 3 && args[1] == "--render-concepts")
            ContentRendered += async (_, _) =>
            {
                try
                {
                    Width = 1450; Height = 1050;
                    Directory.CreateDirectory(args[2]);
                    string[] names = ["tactical-dark", "fluent-dark", "esports-neon"];
                    for (var i = 0; i < names.Length; i++)
                    {
                        ConceptTabs.SelectedIndex = i;
                        await Dispatcher.InvokeAsync(() => UpdateLayout(), DispatcherPriority.ApplicationIdle);
                        var visual = (FrameworkElement)Content;
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),
                            (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(visual);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(args[2], names[i] + ".png"));
                        encoder.Save(file);
                    }
                    Application.Current.Shutdown(0);
                }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"render-error.txt"),error.ToString());
                    Application.Current.Shutdown(1);
                }
            };
    }
}
