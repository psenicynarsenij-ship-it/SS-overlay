using System.IO;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using DotaHpAlert.Core;
using DotaHpAlert.Recognition;
using Microsoft.Win32;

namespace DotaHpAlert.App;

public partial class MainWindow : Window
{
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotaHpAlert", "settings.json");
    private readonly OverlayWindow overlay = new();
    private readonly Queue<string> messages = new();
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private bool initialized, closing, allowClose, acceptAlerts;

    public MainWindow()
    {
        InitializeComponent();
        ApplicationSettings settings;
        try { settings = SettingsStore.Load(settingsPath); }
        catch (Exception error) { settings = new(); Log($"Не удалось загрузить настройки: {error.Message}. Использованы значения по умолчанию; исходный файл сохранён."); }
        Fill(settings); initialized = true;
        Log("Готово. Профиль Invoker включается вручную; автоматического распознавания героя нет.");
    }
    private void Fill(ApplicationSettings s)
    {
        Invoker.IsChecked = s.Alerts.InvokerEnabled; Sound.IsChecked = s.Alerts.SoundEnabled; Visual.IsChecked = s.Alerts.VisualEnabled;
        Threshold.Text = s.Alerts.HpThreshold.ToString(); Repeat.Text = s.Alerts.RepeatInterval.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        Hysteresis.Text = s.Alerts.Hysteresis.ToString(); Confidence.Text = s.MinimumConfidence.ToString(CultureInfo.InvariantCulture);
        Interval.Text = s.CaptureIntervalMs.ToString(); GameTitle.Text = s.WindowTitle;
        RegionX.Text = s.Region.X.ToString(); RegionY.Text = s.Region.Y.ToString();
        RegionWidth.Text = s.Region.Width.ToString(); RegionHeight.Text = s.Region.Height.ToString();
        OverlayX.Text = s.OverlayLeft.ToString(CultureInfo.InvariantCulture); OverlayY.Text = s.OverlayTop.ToString(CultureInfo.InvariantCulture);
    }
    private ApplicationSettings ReadSettings()
    {
        static int Integer(string s) => int.Parse(s, CultureInfo.InvariantCulture);
        static double Number(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);
        var settings = new ApplicationSettings
        {
            Alerts = new() { HpThreshold = Integer(Threshold.Text), RepeatInterval = TimeSpan.FromSeconds(Number(Repeat.Text)),
                Hysteresis = Integer(Hysteresis.Text), InvokerEnabled = Invoker.IsChecked == true,
                SoundEnabled = Sound.IsChecked == true, VisualEnabled = Visual.IsChecked == true },
            Region = new(Integer(RegionX.Text), Integer(RegionY.Text), Integer(RegionWidth.Text), Integer(RegionHeight.Text)),
            WindowTitle = GameTitle.Text.Trim(), CaptureIntervalMs = Integer(Interval.Text), MinimumConfidence = Number(Confidence.Text),
            OverlayLeft = Number(OverlayX.Text), OverlayTop = Number(OverlayY.Text)
        };
        settings.Validate(); return settings;
    }
    private async void StartCapture(object sender, RoutedEventArgs e) => await StartMonitoring(false);
    private async void StartDemo(object sender, RoutedEventArgs e) => await StartMonitoring(true);
    private async Task StartMonitoring(bool demo)
    {
        if (worker is not null || closing) return;
        try
        {
            var settings = ReadSettings();
            acceptAlerts = settings.Alerts.InvokerEnabled;
            if (!acceptAlerts) Log("Профиль Invoker выключен: OCR работает, уведомления отключены.");
            Status.Text = demo ? "Демо: реальные изображения → OCR → AlertEngine" : "Ожидание окна игры на переднем плане…";
            var demoDirectory = demo ? Path.Combine(AppContext.BaseDirectory, "demo") : null;
            await RunWorker(token => MonitorSession.RunAsync(settings, demoDirectory,
                update => Dispatcher.InvokeAsync(() => ShowUpdate(update, settings)).Task, token));
        }
        catch (Exception error) { Log(error.Message); }
    }
    private async Task RunWorker(Func<CancellationToken, Task> action)
    {
        var source = new CancellationTokenSource(); cancellation = source;
        SetRunning(true);
        worker = Task.Run(() => action(source.Token));
        try { await worker; }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception error) { Log($"Ошибка: {error.Message}. Проверьте tessdata, native DLL и VC++ runtime."); }
        finally
        {
            worker = null; cancellation = null; source.Dispose(); acceptAlerts = false;
            overlay.Dismiss(); SetRunning(false); Status.Text += " • остановлено";
        }
    }
    private void SetRunning(bool running)
    {
        SettingsPanel.IsEnabled = !running;
        Sound.IsEnabled = Visual.IsEnabled = !running;
        StartButton.IsEnabled = DemoButton.IsEnabled = ImageButton.IsEnabled = SaveButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
    }
    private async Task StopAsync()
    {
        acceptAlerts = false; overlay.Dismiss();
        cancellation?.Cancel();
        var pending = worker;
        if (pending is null) return;
        try { await pending; }
        catch (OperationCanceledException) { }
        catch (Exception error) { Log(error.Message); }
    }
    private async void Stop(object sender, RoutedEventArgs e) => await StopAsync();
    private async void InvokerChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized || Invoker.IsChecked == true) return;
        acceptAlerts = false; overlay.Dismiss();
        await StopAsync();
        Log("Профиль Invoker отключён: тревога сброшена, захват остановлен.");
    }
    private void ShowUpdate(MonitorUpdate update, ApplicationSettings settings)
    {
        if (closing || cancellation?.IsCancellationRequested == true) return;
        if (update.PreviewPng is not null)
        {
            using var stream = new MemoryStream(update.PreviewPng);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); Preview.Source = bitmap;
        }
        var result = update.Recognition;
        if (result is null)
        {
            Status.Text = update.Message; Preview.Source = null; Timing.Text = "Кадр недоступен"; overlay.Dismiss();
        }
        else
        {
            Status.Text = $"HP: {(result.Reading.IsValid ? result.Reading.CurrentHp.ToString() : "недостоверно")} | {update.Decision?.State} | {update.Decision?.Action}";
            Timing.Text = $"OCR: {result.Duration.TotalMilliseconds:F1} мс | цикл: {update.TotalDuration.TotalMilliseconds:F1} мс | уверенность: {result.Reading.Confidence:P0} | текст: {result.RawText}";
            if (!result.Reading.IsValid) overlay.Dismiss();
        }
        var decision = update.Decision;
        if (decision?.Action == AlertAction.Reset) overlay.Dismiss();
        if (acceptAlerts && Invoker.IsChecked == true && decision?.Action is AlertAction.Show or AlertAction.Repeat)
        {
            if (decision.ShowVisual) overlay.Notify(result!.Reading.CurrentHp, settings.OverlayLeft, settings.OverlayTop);
            if (decision.PlaySound && !MessageBeep(0x30)) Log("Системный звук недоступен.");
        }
        Log($"{update.Message}; {decision?.Reason}");
    }
    private async void OpenImage(object sender, RoutedEventArgs e)
    {
        if (worker is not null || closing) return;
        var dialog = new OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var settings = ReadSettings(); acceptAlerts = false;
            await RunWorker(async token =>
            {
                using var recognizer = new HpRecognizer(Path.Combine(AppContext.BaseDirectory, "tessdata"), new() { MinimumConfidence = settings.MinimumConfidence });
                var image = ImageFrame.Load(dialog.FileName);
                if (image.Width > ImagePreprocessor.MaxWidth || image.Height > ImagePreprocessor.MaxHeight) image = image.Crop(settings.Region);
                var result = recognizer.Recognize(image, DateTimeOffset.UtcNow);
                token.ThrowIfCancellationRequested();
                var update = new MonitorUpdate(result, null, image.ToPng(), result.Duration, "Просмотр OCR: уведомления отключены");
                await Dispatcher.InvokeAsync(() => ShowUpdate(update, settings));
            });
        }
        catch (Exception error) { Log(error.Message); }
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        try { SettingsStore.Save(settingsPath, ReadSettings()); Log($"Настройки сохранены: {settingsPath}"); }
        catch (Exception error) { Log($"Настройки не сохранены: {error.Message}"); }
    }
    private void Log(string message)
    {
        messages.Enqueue($"{DateTime.Now:HH:mm:ss} {message}");
        while (messages.Count > 100) messages.Dequeue();
        Diagnostics.Text = string.Join(Environment.NewLine, messages); Diagnostics.ScrollToEnd();
    }
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true; IsEnabled = false;
        await StopAsync(); overlay.Close(); allowClose = true; Close();
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MessageBeep(uint type);
}
