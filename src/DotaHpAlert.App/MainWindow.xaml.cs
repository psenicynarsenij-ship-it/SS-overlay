using System.IO;
using System.ComponentModel;
using System.Globalization;
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
    private readonly AlertSoundPlayer soundPlayer = new();
    private readonly NotificationRouter notifications;
    private readonly Queue<string> messages = new();
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private bool demoMode, viewingImage;
    private string selectedSound = SoundLibrary.StandardId;
    private string positionPreset = "Custom";
    private bool changingPosition;
    private bool initialized, closing, allowClose, acceptAlerts;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_,_) =>
        {
            var dark=1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle,20,ref dark,4);
        };
        notifications = new NotificationRouter(overlay, soundPlayer, NotificationDiagnostic);
        ApplicationSettings settings;
        try { settings = SettingsStore.Load(settingsPath); }
        catch (Exception error) { settings = new(); Log($"Не удалось загрузить настройки: {error.Message}. Использованы значения по умолчанию; исходный файл сохранён."); }
        Fill(settings); initialized = true;
        soundPlayer.Diagnostic = SoundFallback;
        SoundVolume.ValueChanged += (_,_) => { if(initialized) SaveSoundSelection(); };
        OverlayX.TextChanged += (_,_) => { if (!changingPosition) positionPreset="Custom"; };
        OverlayY.TextChanged += (_,_) => { if (!changingPosition) positionPreset="Custom"; };
        Log("Готово. Профиль Invoker включается вручную; автоматического распознавания героя нет.");
    }
    private void Fill(ApplicationSettings s)
    {
        selectedSound = SoundLibrary.Find(s.SoundPresetId).Id; soundPlayer.PresetId = selectedSound;
        Presets.ItemsSource=SoundLibrary.Presets;
        Presets.SelectedItem=SoundLibrary.Find(selectedSound);
        SoundName.Text = SoundLibrary.Find(selectedSound).Name;
        positionPreset = s.OverlayPosition; AnimationEnabled.IsChecked=s.OverlayAnimation;
        overlay.AnimationEnabled=s.OverlayAnimation;
        SoundVolume.Value = s.SoundVolume;
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
            SoundPresetId = selectedSound, OverlayPosition=positionPreset,
            OverlayAnimation=AnimationEnabled.IsChecked==true,
            SoundVolume = SoundVolume.Value,
            OverlayLeft = Number(OverlayX.Text), OverlayTop = Number(OverlayY.Text)
        };
        settings.Validate(); return settings;
    }
    private async void StartCapture(object sender, RoutedEventArgs e)
    {
        if(worker is not null) await StopAsync();
        else await StartMonitoring(false);
    }
    private async void StartDemo(object sender, RoutedEventArgs e) => await StartMonitoring(true);
    private async Task StartMonitoring(bool demo)
    {
        if (worker is not null || closing) return;
        try
        {
            var settings = ReadSettings();
            notifications.Stop();
            overlay.AnimationEnabled=settings.OverlayAnimation;
            soundPlayer.PresetId=selectedSound;
            acceptAlerts = settings.Alerts.InvokerEnabled;
            if (!acceptAlerts) Log("Профиль Invoker выключен: OCR работает, уведомления отключены.");
            Status.Text = demo ? "Демо: реальные изображения → OCR → AlertEngine" : "Ожидание окна игры на переднем плане…";
            demoMode=demo; viewingImage=false;
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
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        worker = finished.Task; // Stop awaits UI cleanup as well as the native worker.
        try { await Task.Run(() => action(source.Token)); }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception error) { Log($"Ошибка: {error.Message}. Проверьте tessdata, native DLL и VC++ runtime."); }
        finally
        {
            try
            {
                acceptAlerts = false; notifications.Stop(); SetRunning(false); Status.Text += " • остановлено";
            }
            finally
            {
                worker = null; cancellation = null; source.Dispose(); finished.TrySetResult();
            }
        }
    }
    private void SetRunning(bool running)
    {
        SettingsPanel.IsEnabled = AlertFields.IsEnabled = PositionPanel.IsEnabled = SoundPanel.IsEnabled = AnimationEnabled.IsEnabled = !running;
        StartButton.Content=running ? "Остановить" : "Начать мониторинг";
        StartButton.IsEnabled=true;
        if (!running) GameState.Text="Окно игры: мониторинг остановлен";
        Sound.IsEnabled = Visual.IsEnabled = !running;
        DemoButton.IsEnabled = ImageButton.IsEnabled = SaveButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
    }
    private async Task StopAsync()
    {
        acceptAlerts = false; notifications.Stop();
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
        acceptAlerts = false; notifications.Stop();
        await StopAsync();
        Log("Профиль Invoker отключён: тревога сброшена, захват остановлен.");
    }
    private void ShowUpdate(MonitorUpdate update, ApplicationSettings settings)
    {
        if (closing || cancellation?.IsCancellationRequested == true) return;
        if (update.Decision is { } routedDecision)
            notifications.Route(routedDecision, update.Recognition?.Reading.CurrentHp ?? 0,
                new(settings.OverlayLeft, settings.OverlayTop, settings.SoundVolume),
                acceptAlerts && Invoker.IsChecked == true);
        if (update.PreviewPng is not null)
        {
            using var stream = new MemoryStream(update.PreviewPng);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); Preview.Source = bitmap;
        }
        var result = update.Recognition;
        CurrentHp.Text = result?.Reading.IsValid==true ? $"{result.Reading.CurrentHp} HP" : "— HP";
        RecognitionConfidence.Text = result is null ? "Уверенность OCR: —" : $"Уверенность OCR: {result.Reading.Confidence:P0}";
        GameState.Text=result is null ? "Источник кадров недоступен" : viewingImage ? "Источник: файл изображения" : demoMode ? "Источник: демонстрационные изображения" : "Окно Dota 2: кадр получен";
        if (result is null)
        {
            Status.Text = update.Message; Preview.Source = null; Timing.Text = "Кадр недоступен"; notifications.Stop();
        }
        else
        {
            Status.Text = viewingImage ? "Просмотр OCR изображения" : result.Reading.IsValid ? "Мониторинг активен" : "Мониторинг активен · данные недостоверны";
            Timing.Text = $"OCR: {result.Duration.TotalMilliseconds:F1} мс | цикл: {update.TotalDuration.TotalMilliseconds:F1} мс | уверенность: {result.Reading.Confidence:P0} | текст: {result.RawText}";
        }
        Log($"{update.Message}; {update.Decision?.Reason}");
    }
    private async void TestNotification(object sender, RoutedEventArgs e)
    {
        if (closing) return;
        try
        {
            if (worker is not null)
            {
                Log("Тест уведомления: останавливаю захват, чтобы он не сбросил ручное предупреждение.");
                await StopAsync();
            }
            if (closing) return;
            overlay.AnimationEnabled=AnimationEnabled.IsChecked==true;
            soundPlayer.PresetId=selectedSound;
            // Deliberately independent of OCR settings, engine state, profile and channel switches.
            var left = double.Parse(OverlayX.Text.Replace(',', '.'), CultureInfo.InvariantCulture);
            var top = double.Parse(OverlayY.Text.Replace(',', '.'), CultureInfo.InvariantCulture);
            notifications.Test(new(left, top, SoundVolume.Value));
        }
        catch (Exception error) { Log($"Тест уведомления: {error}"); }
    }
    private void NotificationDiagnostic(NotificationEvent entry)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.InvokeAsync(() => NotificationDiagnostic(entry));
            return;
        }
        if(entry.Kind==NotificationEventKind.EngineIssued) LastAlert.Text=$"Последнее предупреждение: {DateTime.Now:HH:mm:ss} · {entry.Message}";
        Log($"[{entry.Kind}] {entry.Message}");
    }

    private async void OpenImage(object sender, RoutedEventArgs e)
    {
        if (worker is not null || closing) return;
        var dialog = new OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var settings = ReadSettings(); acceptAlerts = false; viewingImage=true;
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
    private void SetPosition(string preset)
    {
        changingPosition=true;
        try
        {
            positionPreset=preset;
            if(preset!="Custom")
            {
                OverlayX.Text=(preset=="TopCenter" ? SystemParameters.WorkArea.Left+(SystemParameters.WorkArea.Width-330)/2 : SystemParameters.WorkArea.Left+30).ToString(CultureInfo.InvariantCulture);
                OverlayY.Text=(SystemParameters.WorkArea.Top+30).ToString(CultureInfo.InvariantCulture);
            }
        }
        finally { changingPosition=false; }
    }
    private void PositionTopLeft(object sender,RoutedEventArgs e)=>SetPosition("TopLeft");
    private void PositionTopCenter(object sender,RoutedEventArgs e)=>SetPosition("TopCenter");
    private void PositionCustom(object sender,RoutedEventArgs e)=>SetPosition("Custom");
    private void SelectPreset(object sender,RoutedEventArgs e)
    {
        if(sender is System.Windows.Controls.Button { Tag: SoundPreset preset }) Presets.SelectedItem=preset;
    }
    private void PresetChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if(Presets.SelectedItem is not SoundPreset preset) return;
        selectedSound=preset.Id; soundPlayer.PresetId=preset.Id; SoundName.Text=preset.Name;
        if(!initialized) return;
        SaveSoundSelection();
    }
    private void SaveSoundSelection()
    {
        try
        {
            var saved=SettingsStore.Load(settingsPath);
            SettingsStore.Save(settingsPath,saved with { SoundPresetId=selectedSound,SoundVolume=SoundVolume.Value });
        }
        catch(Exception error) { Log($"Не удалось сохранить звук: {error.Message}"); }
    }
    private void PreviewPreset(object sender,RoutedEventArgs e)
    {
        if(closing || worker is not null) return;
        if(sender is not System.Windows.Controls.Button { Tag: SoundPreset preset }) return;
        notifications.Stop();
        soundPlayer.PresetId=preset.Id;
        notifications.PreviewSound(SoundVolume.Value);
        soundPlayer.PresetId=selectedSound;
    }
    private void SoundFallback(string message)
    {
        selectedSound=SoundLibrary.StandardId;
        Presets.SelectedItem=SoundLibrary.Find(selectedSound); SoundName.Text="Стандартный сигнал";
        Log(message);
        try { if(File.Exists(settingsPath)) SettingsStore.Save(settingsPath,SettingsStore.Load(settingsPath) with { SoundPresetId=SoundLibrary.StandardId }); }
        catch(Exception error) { Log($"Не удалось сохранить fallback: {error.Message}"); }
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    private void Log(string message)
    {
        messages.Enqueue($"{DateTime.Now:HH:mm:ss} {message}");
        while (messages.Count > 100) messages.Dequeue();
        Diagnostics.Text = string.Join(Environment.NewLine, messages); Diagnostics.ScrollToEnd();
    }
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (closing) { e.Cancel=true; return; }
        closing = true; IsEnabled = false;
        if(worker is null)
        {
            notifications.Stop();
            try { soundPlayer.Dispose(); } catch(Exception error) { Log(error.Message); }
            overlay.Close(); allowClose=true; return;
        }
        e.Cancel=true;
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        await StopAsync();
        try { soundPlayer.Dispose(); }
        catch (Exception error) { Log($"[SoundError] {error}"); }
        overlay.Close(); allowClose = true; Close();
    }
}
