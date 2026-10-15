using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf = System.Windows;
using Media = System.Windows.Media;
using Control = System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace XboxRapidFire;

public partial class MainWindow : Wpf.Window
{
    private readonly AppSettings config;
    internal ButtonModel[] Buttons { get; }
    private readonly bool testing;
    private readonly Forms.NotifyIcon tray;
    private readonly Icon trayIcon;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private Bridge? bridge;
    private bool busy, paused, ready, hotkey, closeReady, exitRequested, exitInProgress;
    private IntPtr hwnd;
    private int detectionCounter;
    private string? busyAction;
    private string idleDetail = "先启动连发助手，再打开游戏。";
    public string? LastError { get; private set; }
    internal bool Active => bridge?.Running == true;
    internal bool Paused => paused;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr handle, int id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr handle, IntPtr deviceContext, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

    public MainWindow() : this(false) { }
    internal MainWindow(bool test)
    {
        testing = test; config = test ? new() : Store.Load();
        Loc.SetLanguage(config.Language);
        Wpf.Application.Current.Resources.MergedDictionaries.Add(Loc.Resources);
        Buttons = config.Keys.Select((setting, index) => new ButtonModel(index, setting)).ToArray();
        InitializeComponent(); DataContext = new { Buttons };
        Icon = AppIcons.WindowIcon();
        AutoStart.IsChecked = config.AutoStart;
        foreach (var b in Buttons) b.Changed += ConfigChanged;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("打开窗口"), null, (_, _) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add(Loc.T("暂停 / 恢复连发"), null, (_, _) => Dispatcher.Invoke(TogglePause));
        menu.Items.Add(Loc.T("退出并恢复手柄"), null, (_, _) => Dispatcher.Invoke(RequestExit));
        trayIcon = AppIcons.TrayIcon();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = Loc.T("PULSE · Xbox 连发助手"), Visible = true, ContextMenuStrip = menu };
        RefreshLanguage();
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            int dark = 1; _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            // Explicit colors prevent the Windows accent from painting the active caption cyan.
            SetFrameColor(34, 0x3A3830);
            SetFrameColor(35, 0x151310);
            SetFrameColor(36, 0xEFF2ED);
            HwndSource.FromHwnd(hwnd)?.AddHook(HandleMessage);
            hotkey = RegisterHotKey(hwnd, 81, 0x4000, (uint)Forms.Keys.F8);
            PauseButton.Content = Loc.T(hotkey ? "暂停   F8" : "暂停连发");
        };
        StateChanged += (_, _) => { if (WindowState == Wpf.WindowState.Minimized) HideToTray(); };
        timer.Tick += async (_, _) => await Tick();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Save(); };
        Loaded += async (_, _) =>
        {
            ready = true; ConfigChanged(); RefreshDrivers();
            if (!testing) await Restore();
            timer.Start(); Detect();
            if (!testing && LastError is null && config.AutoStart && config.Keys.Any(k => k.Enabled) && Hiding.CliPath is not null) await StartBridge();
        };
        Closing += OnClosing;
    }
    private IntPtr HandleMessage(IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message == 0x0312 && w.ToInt32() == 81) { TogglePause(); handled = true; }
        return IntPtr.Zero;
    }
    private void ShowWindow() { Show(); WindowState = Wpf.WindowState.Normal; Activate(); }
    private void HideToTray()
    {
        if (Keyboard.FocusedElement is Control.TextBox input)
            input.GetBindingExpression(Control.TextBox.TextProperty)?.UpdateSource();
        Save(); Hide();
    }
    internal void RequestExit()
    {
        exitRequested = true;
        if (!busy && !exitInProgress) Close();
    }
    private void ConfigChanged()
    {
        if (!ready) return;
        config.Keys = Buttons.Select(b => new KeySetting(b.Enabled, b.Interval, b.ToggleMode)).ToArray();
        config.AutoStart = AutoStart.IsChecked == true;
        EnabledCount.Text = config.Keys.Count(k => k.Enabled).ToString();
        bridge?.Update(Runtime());
        saveTimer.Stop(); saveTimer.Start();
    }
    private void AutoChanged(object sender, Wpf.RoutedEventArgs e) => ConfigChanged();
    private void LanguageClick(object sender, Wpf.RoutedEventArgs e)
    {
        config.Language = (string)((Control.Button)sender).Tag;
        Loc.SetLanguage(config.Language);
        RefreshLanguage(); Save();
    }
    private void RefreshLanguage()
    {
        foreach (var button in Buttons) button.RefreshLanguage();
        var labels = new[] { "打开窗口", "暂停 / 恢复连发", "退出并恢复手柄" };
        for (int i = 0; i < labels.Length; i++) tray.ContextMenuStrip!.Items[i].Text = Loc.T(labels[i]);
        tray.Text = Loc.T("PULSE · Xbox 连发助手");
        foreach (var button in new[] { ChineseButton, EnglishButton })
        {
            bool selected = (string)button.Tag == Loc.Language;
            button.Background = (Media.Brush)FindResource(selected ? "Accent" : "Surface");
            button.Foreground = new Media.SolidColorBrush(selected ? Media.Color.FromRgb(25, 39, 23) : Media.Color.FromRgb(231, 238, 234));
        }
        StartButton.Content = Loc.T(bridge is null ? "开始连发" : "停止连发");
        PauseButton.Content = Loc.T(paused ? (hotkey ? "继续   F8" : "继续连发") : (hotkey ? "暂停   F8" : "暂停连发"));
        if (busy)
        {
            StatusBadge.Text = Loc.T("处理中"); StatusTitle.Text = Loc.T(busyAction ?? "正在处理，请稍候…");
            StatusDetail.Text = Loc.T("正在处理，请稍候…");
        }
        else if (LastError is not null) ShowError();
        else if (bridge is not null) UpdateState();
        else
        {
            StatusBadge.Text = Loc.T("待启动"); StatusTitle.Text = Loc.T("准备好后，开始连发");
            StatusDetail.Text = Loc.T(idleDetail);
        }
        RefreshDrivers();
        if (bridge is null) Detect();
    }
    private void Save()
    {
        if (testing) return;
        try { Store.Write(Store.SettingsPath, config); }
        catch (Exception ex) { Error("无法保存设置：" + ex.Message); }
    }
    private RuntimeSettings Runtime() => new(config.Keys.ToArray(), config.TriggerThreshold, paused);
    private void Busy(bool value, string? action = null)
    {
        busy = value;
        StartButton.IsEnabled = !value; PauseButton.IsEnabled = !value && bridge is not null;
        RestoreButton.IsEnabled = !value; InstallButton.IsEnabled = !value && bridge is null;
        if (action is not null) { busyAction = action; StatusTitle.Text = Loc.T(action); StatusDetail.Text = Loc.T("正在处理，请稍候…"); StatusBadge.Text = Loc.T("处理中"); }
        if (!value && exitRequested && !exitInProgress && !closeReady)
            _ = Dispatcher.BeginInvoke(new Action(Close));
    }
    internal async Task StartBridge()
    {
        if (busy || bridge is not null) return;
        Wpf.Input.Keyboard.ClearFocus();
        if (Buttons.Any(b => b.Error is not null)) { Error("请先修正卡片中标出的连发周期，范围为 30–5000 ms。"); return; }
        if (!config.Keys.Any(k => k.Enabled)) { Error("请先打开至少一个按键卡片右上角的开关。"); return; }
        Busy(true, "正在连接手柄"); paused = false; Save();
        var b = new Bridge(Runtime());
        try
        {
            await Task.Run(b.Start); bridge = b; StartButton.Content = Loc.T("停止连发");
            LastError = null; UpdateState();
        }
        catch (Exception ex)
        {
            Store.Log(ex.ToString());
            Error(ex.GetType().Name.Contains("vigem", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("bus", StringComparison.OrdinalIgnoreCase)
                ? "虚拟手柄驱动未就绪。请安装左侧所需驱动，按提示重启电脑后再试。" : ex.Message);
        }
        finally { Busy(false); RefreshDrivers(); }
    }
    internal async Task StopBridge()
    {
        if (busy) return;
        Busy(true, "正在恢复普通手柄输入");
        try
        {
            if (bridge is not null) await Task.Run(bridge.Dispose);
            bridge = null; paused = false; LastError = null; Idle();
        }
        catch (Exception ex) { Error(ex.Message); }
        finally { Busy(false); }
    }
    private async Task Restore()
    {
        if (bridge is not null || busy || !File.Exists(Store.JournalPath)) return;
        Busy(true, "正在恢复手柄");
        try { await Task.Run(Hiding.Recover); LastError = null; Idle(); }
        catch (Exception ex) { Error("恢复手柄失败：" + ex.Message); }
        finally { Busy(false); }
    }
    internal void TogglePause()
    {
        if (busy || bridge is null) return;
        paused = !paused; bridge.Update(Runtime());
        PauseButton.Content = Loc.T(paused ? (hotkey ? "继续   F8" : "继续连发") : (hotkey ? "暂停   F8" : "暂停连发"));
        UpdateState();
    }
    private void Idle()
    {
        StartButton.Content = Loc.T("开始连发"); PauseButton.Content = Loc.T(hotkey ? "暂停   F8" : "暂停连发");
        StatusBadge.Text = Loc.T("待启动"); StatusTitle.Text = Loc.T("准备好后，开始连发");
        idleDetail = "先启动连发助手，再打开游戏。";
        StatusDetail.Text = Loc.T(idleDetail); StatusTitle.Foreground = Wpf.Media.Brushes.White;
        foreach (var b in Buttons) b.SetState(false, false, false);
    }
    private void Error(string message)
    {
        LastError = message; ShowError(); Store.Log(message);
    }
    private void ShowError()
    {
        StatusBadge.Text = Loc.T("需要处理"); StatusTitle.Text = Loc.T("暂时无法启动");
        StatusTitle.Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(255, 181, 146));
        StatusDetail.Text = Loc.T(LastError!);
    }
    private async Task Tick()
    {
        if (busy) return;
        if (bridge?.Failure is { } failure) { await StopBridge(); Error(failure); return; }
        if (bridge is not null) UpdateState();
        else if (++detectionCounter % 12 == 0) Detect();
        if (detectionCounter % 60 == 0 && bridge is null) RefreshDrivers();
    }
    private void Detect()
    {
        var slots = XInput.Connected();
        ConnectionText.Text = Loc.T(slots.Length == 1 ? "已连接 · Xbox 手柄" : slots.Length == 0 ? "未连接 · 请连接手柄" : "检测到多个手柄");
        ConnectionDot.Fill = new Media.SolidColorBrush(slots.Length == 1 ? Media.Color.FromRgb(165, 235, 131) : Media.Color.FromRgb(230, 185, 132));
    }
    private void UpdateState()
    {
        if (bridge is null) return;
        StatusBadge.Text = Loc.T(paused ? "已暂停" : "连发中");
        StatusTitle.Text = Loc.T(paused ? "普通手柄输入 · 连发已暂停" : "连发已就绪，按卡片模式操作");
        StatusTitle.Foreground = (Media.Brush)FindResource(paused ? "Muted" : "Accent");
        StatusDetail.Text = Loc.T(paused ? "持续连发已清除。继续后，单击模式需再次按下开始。" : "长按模式：松开停止。单击模式：按一次开始，再按一次停止。");
        ConnectionText.Text = Loc.T("已连接 · 输入正常");
        var s = bridge.Snapshot;
        foreach (var b in Buttons) b.SetState(Keys8.Held(b.Index, s.Input), Keys8.Held(b.Index, s.Output), !paused, (s.LatchedMask & (1 << b.Index)) != 0);
    }
    private void RefreshDrivers()
    {
        BusText.Text = Loc.T("虚拟手柄驱动  ·  ") + Loc.T(DriverSetup.BusInstalled ? "已安装" : "未安装");
        HideText.Text = Loc.T("防重复输入  ·  ") + Loc.T(Hiding.CliPath is not null ? "已安装" : "未安装");
        InstallButton.Content = Loc.T(DriverSetup.BusInstalled && Hiding.CliPath is not null ? "检查连接环境" : "安装所需驱动");
    }
    private async void StartClick(object sender, Wpf.RoutedEventArgs e) { if (bridge is null) await StartBridge(); else await StopBridge(); }
    private void PauseClick(object sender, Wpf.RoutedEventArgs e) => TogglePause();
    private async void RestoreClick(object sender, Wpf.RoutedEventArgs e) { await StopBridge(); await Restore(); }
    private async void InstallClick(object sender, Wpf.RoutedEventArgs e)
    {
        if (busy || bridge is not null) return;
        Busy(true, "请完成 Windows 驱动安装向导");
        try
        {
            await DriverSetup.Install(); LastError = null; Idle();
            idleDetail = "驱动安装完成。如果安装向导提示重启，请先重启电脑。";
            StatusDetail.Text = Loc.T(idleDetail);
        }
        catch (Exception ex) { Error(ex.Message); }
        finally { Busy(false); RefreshDrivers(); }
    }
    private void MinusClick(object sender, Wpf.RoutedEventArgs e) => ((ButtonModel)((Control.Button)sender).Tag).Step(-10);
    private void PlusClick(object sender, Wpf.RoutedEventArgs e) => ((ButtonModel)((Control.Button)sender).Tag).Step(10);
    private void ModeClick(object sender, Wpf.RoutedEventArgs e)
    {
        var button = (ButtonModel)((Control.Button)sender).Tag;
        button.ToggleMode = !button.ToggleMode;
    }
    private void IntervalKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ((Control.TextBox)sender).GetBindingExpression(Control.TextBox.TextProperty)?.UpdateSource();
        Wpf.Input.Keyboard.ClearFocus(); e.Handled = true;
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeReady) return;
        e.Cancel = true;
        if (!exitRequested) { HideToTray(); return; }
        if (busy || exitInProgress) return;
        exitInProgress = true;
        timer.Stop(); saveTimer.Stop(); Save();
        await StopBridge(); if (!testing) await Restore();
        if (bridge is not null || File.Exists(Store.JournalPath))
        {
            StatusTitle.Text = Loc.T("请先恢复手柄再退出");
            exitRequested = false; exitInProgress = false;
            ShowWindow(); timer.Start(); return;
        }
        if (hotkey) UnregisterHotKey(hwnd, 81);
        tray.Visible = false; tray.Dispose(); trayIcon.Dispose(); closeReady = true;
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
    internal void TestConfigure(int i, bool enable, int ms) { Buttons[i].Enabled = enable; Buttons[i].IntervalText = ms.ToString(); }
    private int SetFrameColor(int attribute, int color) => DwmSetWindowAttribute(hwnd, attribute, ref color, sizeof(int));
    internal bool TestChromeColors => new[] { (34, 0x3A3830), (35, 0x151310), (36, 0xEFF2ED) }
        .All(pair => SetFrameColor(pair.Item1, pair.Item2) == 0);
    internal PulseSwitch TestSwitch(int index)
    {
        var pending = new Stack<Wpf.DependencyObject>(); pending.Push(ButtonCards);
        while (pending.Count > 0)
        {
            var item = pending.Pop();
            if (item is PulseSwitch toggle && ReferenceEquals(toggle.DataContext, Buttons[index])) return toggle;
            for (int i = 0; i < Media.VisualTreeHelper.GetChildrenCount(item); i++)
                pending.Push(Media.VisualTreeHelper.GetChild(item, i));
        }
        throw new InvalidOperationException("Toggle template was not created.");
    }
    internal void TestWindowSnapshot(string path)
    {
        if (!GetWindowRect(hwnd, out var rect)) throw new IOException("Could not read window bounds.");
        using var image = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
        using var graphics = Graphics.FromImage(image);
        var dc = graphics.GetHdc();
        try { if (!PrintWindow(hwnd, dc, 2)) throw new IOException("Could not capture window."); }
        finally { graphics.ReleaseHdc(dc); }
        image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    internal void TestSnapshot(string path)
    {
        UpdateLayout(); var dpi = Media.VisualTreeHelper.GetDpi(this);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(Root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(Root.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, Media.PixelFormats.Pbgra32);
        bmp.Render(Root); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(path); png.Save(stream);
    }
    internal void TestReadyState() { LastError = null; Idle(); }
    internal bool TestTrayVisible => tray.Visible;
    internal void TestShowWindow() => ShowWindow();
    internal void TestTrayExit() => tray.ContextMenuStrip!.Items[2].PerformClick();
    internal bool TestHasCustomIcons => Icon is not null && tray.Icon is not null;
    internal string TestLanguage => config.Language;
    internal RuntimeSettings TestRuntime => Runtime();
    internal string[] TestTrayLabels => tray.ContextMenuStrip!.Items.Cast<Forms.ToolStripItem>().Select(item => item.Text!).ToArray();
}
