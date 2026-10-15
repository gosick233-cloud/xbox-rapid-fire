using System.Diagnostics;
using System.Text.Json;

namespace XboxRapidFire;

internal static class SelfTest
{
    private static RuntimeSettings Config(params (int Index, int Ms)[] keys)
    {
        var result = Enumerable.Range(0, 8).Select(_ => new KeySetting()).ToArray();
        foreach (var (i, ms) in keys) result[i] = new(true, ms);
        return new(result);
    }
    public static int Run(string path)
    {
        var tests = new List<object>(); int failed = 0;
        void Check(string name, Action assertion)
        {
            try { assertion(); tests.Add(new { Name = name, Passed = true, Error = "" }); }
            catch (Exception ex) { failed++; tests.Add(new { Name = name, Passed = false, Error = ex.Message }); }
        }
        static void Require(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
        var a = new Pad { Buttons = 0x1000 };
        Check("Immediate first down / exact half-period off / next period down", () =>
        {
            var e = new TurboEngine(); var c = Config((3, 100));
            foreach (var (t, expected) in new[] { (0d, true), (49.9, true), (50d, false), (99.9, false), (100d, true), (150d, false) })
                Require(Keys8.Held(3, e.Apply(a, c, t)) == expected, $"phase {t}");
        });
        Check("Release stops immediately during down half", () =>
        {
            var e = new TurboEngine(); var c = Config((3, 100)); e.Apply(a, c, 0);
            Require(e.Apply(default, c, 12).Buttons == 0);
        });
        Check("New physical press restarts phase after short release", () =>
        {
            var e = new TurboEngine(); var c = Config((3, 100)); e.Apply(a, c, 0); e.Apply(default, c, 51);
            Require(Keys8.Held(3, e.Apply(a, c, 52))); Require(!Keys8.Held(3, e.Apply(a, c, 102)));
        });
        Check("Independent simultaneous button intervals", () =>
        {
            var e = new TurboEngine(); var c = Config((3, 100), (0, 200)); var p = new Pad { Buttons = 0x5000 };
            e.Apply(p, c, 0); var r = e.Apply(p, c, 75);
            Require(!Keys8.Held(3, r) && Keys8.Held(0, r));
            r = e.Apply(p, c, 125); Require(Keys8.Held(3, r) && !Keys8.Held(0, r));
        });
        Check("Separate first-down timestamps for overlapping presses", () =>
        {
            var e = new TurboEngine(); var c = Config((3, 100), (0, 100)); e.Apply(a, c, 0);
            var p = new Pad { Buttons = 0x5000 }; e.Apply(p, c, 40); var r = e.Apply(p, c, 60);
            Require(!Keys8.Held(3, r) && Keys8.Held(0, r));
        });
        Check("LT/RT pulse while preserving analog values and independent rates", () =>
        {
            var e = new TurboEngine(); var c = Config((6, 100), (7, 200)); var p = new Pad { LeftTrigger = 124, RightTrigger = 211 };
            Require(e.Apply(p, c, 0).Equals(p));
            var r = e.Apply(p, c, 75); Require(r.LeftTrigger == 0 && r.RightTrigger == 211);
            r = e.Apply(p, c, 125); Require(r.LeftTrigger == 124 && r.RightTrigger == 0);
        });
        Check("Trigger threshold boundary and release", () =>
        {
            var e = new TurboEngine(); var c = Config((6, 100));
            e.Apply(new Pad { LeftTrigger = 31 }, c, 0);
            Require(e.Apply(new Pad { LeftTrigger = 30 }, c, 75).LeftTrigger == 30);
            Require(e.Apply(new Pad { LeftTrigger = 31 }, c, 76).LeftTrigger == 31);
            Require(e.Apply(default, c, 77).LeftTrigger == 0);
        });
        Check("Non-turbo controls pass through exactly", () =>
        {
            var e = new TurboEngine(); var c = Config(); var p = new Pad { Buttons = 0xF3FF, LeftTrigger = 83, RightTrigger = 255, LeftX = -32768, LeftY = 32767, RightX = -23456, RightY = 3210 };
            Require(e.Apply(p, c, 0).Equals(p)); Require(e.Apply(p, c, 212).Equals(p));
        });
        Check("Axes/D-pad/menu survive a turbo off interval", () =>
        {
            var e = new TurboEngine(); var c = Config((3, 100));
            var p = new Pad { Buttons = 0x1031, LeftX = -32768, RightY = 32767, RightTrigger = 18 };
            e.Apply(p, c, 0); var r = e.Apply(p, c, 75);
            Require(r.Buttons == 0x31 && r.LeftX == -32768 && r.RightY == 32767 && r.RightTrigger == 18);
        });
        Check("Pause returns held button to normal and resume resets phase", () =>
        {
            var e = new TurboEngine(); var c = Config((3, 100)); e.Apply(a, c, 0);
            Require(!Keys8.Held(3, e.Apply(a, c, 75)));
            Require(Keys8.Held(3, e.Apply(a, c with { Paused = true }, 76)));
            Require(Keys8.Held(3, e.Apply(a, c, 77)));
        });
        Check("Changing interval while held starts a fresh down phase", () =>
        {
            var e = new TurboEngine(); e.Apply(a, Config((3, 100)), 0);
            Require(!Keys8.Held(3, e.Apply(a, Config((3, 100)), 75)));
            Require(Keys8.Held(3, e.Apply(a, Config((3, 200)), 76)));
            Require(!Keys8.Held(3, e.Apply(a, Config((3, 200)), 176)));
        });
        Check("Disabling held key passes through, reenabling resets phase", () =>
        {
            var e = new TurboEngine(); var c = Config((3, 100)); e.Apply(a, c, 0);
            Require(e.Apply(a, Config(), 75).Equals(a)); Require(e.Apply(a, c, 76).Equals(a));
        });
        Check("Odd-millisecond cycle boundaries", () =>
        {
            var e = new TurboEngine(); var c = Config((3, 31)); e.Apply(a, c, 0);
            Require(Keys8.Held(3, e.Apply(a, c, 15))); Require(!Keys8.Held(3, e.Apply(a, c, 15.5)));
            Require(Keys8.Held(3, e.Apply(a, c, 31)));
        });
        Check("Maximum interval and long elapsed time have no tick drift", () =>
        {
            var e = new TurboEngine(); var c = Config((3, 5000)); e.Apply(a, c, 1e8);
            Require(Keys8.Held(3, e.Apply(a, c, 1e8 + 86400000)));
            Require(!Keys8.Held(3, e.Apply(a, c, 1e8 + 86402500)));
        });
        Check("Reset releases timing state", () =>
        {
            var e = new TurboEngine(); var c = Config((3, 100)); e.Apply(a, c, 0); e.Reset();
            Require(Keys8.Held(3, e.Apply(a, c, 75)));
        });
        RuntimeSettings Click(int index = 3, int interval = 100)
        {
            var keys = Config().Keys; keys[index] = new(true, interval, true); return new(keys);
        }
        Check("All eight click modes continue after release and consume second press", () =>
        {
            for (int i = 0; i < 8; i++)
            {
                var e = new TurboEngine(); var c = Click(i);
                var down = i < 6 ? new Pad { Buttons = Keys8.All[i].Mask }
                    : i == 6 ? new Pad { LeftTrigger = 147 } : new Pad { RightTrigger = 237 };
                Require(Keys8.Held(i, e.Apply(down, c, 0)));
                Require(Keys8.Held(i, e.Apply(default, c, 25)), $"{i}: release must keep pulsing");
                Require(!Keys8.Held(i, e.Apply(default, c, 50)));
                Require(Keys8.Held(i, e.Apply(default, c, 100)));
                Require(!Keys8.Held(i, e.Apply(down, c, 110)), $"{i}: cancellation press must not leak");
                Require(e.LatchedMask == 0);
                Require(!Keys8.Held(i, e.Apply(down, c, 200)));
                Require(!Keys8.Held(i, e.Apply(default, c, 201)));
                Require(Keys8.Held(i, e.Apply(down, c, 202)), $"{i}: third press starts again");
            }
        });
        Check("Holding first click never toggles repeatedly", () =>
        {
            var e = new TurboEngine(); var c = Click(); e.Apply(a, c, 0);
            Require(!Keys8.Held(3, e.Apply(a, c, 75)));
            Require(Keys8.Held(3, e.Apply(a, c, 300)) && e.LatchedMask == 8);
        });
        Check("Mixed click and hold modes have independent timing and cancellation", () =>
        {
            var keys = Config((0, 200)).Keys; keys[3] = new(true, 100, true); var c = new RuntimeSettings(keys);
            var e = new TurboEngine(); e.Apply(a, c, 0); var x = new Pad { Buttons = 0x4000, LeftX = 1234 };
            var r = e.Apply(x, c, 25); Require(r.Buttons == 0x5000 && r.LeftX == 1234);
            r = e.Apply(x, c, 75); Require(r.Buttons == 0x4000);
            r = e.Apply(default, c, 100); Require(r.Buttons == 0x1000);
            Require(e.Apply(a, c, 101).Buttons == 0 && e.LatchedMask == 0);
        });
        Check("Two latched buttons can be stopped independently", () =>
        {
            var keys = Click().Keys; keys[0] = new(true, 200, true); var c = new RuntimeSettings(keys);
            var e = new TurboEngine(); e.Apply(new Pad { Buttons = 0x5000 }, c, 0); e.Apply(default, c, 1);
            var r = e.Apply(a, c, 25); Require(r.Buttons == 0x4000 && e.LatchedMask == 1);
            r = e.Apply(default, c, 200); Require(r.Buttons == 0x4000);
        });
        Check("Latched triggers send full presses even after a shallow activation edge", () =>
        {
            var e = new TurboEngine(); var c = Click(6); e.Apply(new Pad { LeftTrigger = 31 }, c, 0);
            Require(e.Apply(default, c, 25).LeftTrigger == 255);
            Require(e.Apply(default, c, 50).LeftTrigger == 0);
            Require(e.Apply(default, c, 100).LeftTrigger == 255);
            Require(e.Apply(new Pad { LeftTrigger = 30 }, c, 101).LeftTrigger == 255);
            Require(e.Apply(new Pad { LeftTrigger = 31 }, c, 102).LeftTrigger == 0);
        });
        Check("Pause clears latch and resume requires a fresh physical press", () =>
        {
            var e = new TurboEngine(); var c = Click(); e.Apply(a, c, 0); e.Apply(default, c, 1);
            Require(e.Apply(default, c with { Paused = true }, 25).Buttons == 0 && e.LatchedMask == 0);
            Require(e.Apply(default, c, 100).Buttons == 0);
            e.Apply(a, c with { Paused = true }, 101);
            Require(e.Apply(a, c, 102).Buttons == 0);
            e.Apply(default, c, 103); Require(e.Apply(a, c, 104).Buttons == 0x1000);
        });
        Check("Disable and reenable clear latch without manufacturing a click", () =>
        {
            var e = new TurboEngine(); var c = Click(); e.Apply(a, c, 0); e.Apply(default, c, 1);
            e.Apply(a, Config(), 2); Require(e.LatchedMask == 0);
            Require(e.Apply(a, c, 3).Buttons == 0 && e.LatchedMask == 0);
            e.Apply(default, c, 4); Require(e.Apply(a, c, 5).Buttons == 0x1000);
        });
        Check("Mode changes cancel latch and require release before click activation", () =>
        {
            var e = new TurboEngine(); var c = Click(); e.Apply(a, Config((3, 100)), 0);
            Require(e.Apply(a, c, 1).Buttons == 0);
            e.Apply(default, c, 2); e.Apply(a, c, 3); e.Apply(default, c, 4);
            Require(e.Apply(default, Config((3, 100)), 5).Buttons == 0 && e.LatchedMask == 0);
            Require(e.Apply(default, c, 100).Buttons == 0);
        });
        Check("Rate edits preserve active click mode and restart its pulse phase", () =>
        {
            var e = new TurboEngine(); e.Apply(a, Click(), 0); e.Apply(default, Click(), 75);
            Require(e.Apply(default, Click(interval: 200), 76).Buttons == 0x1000 && e.LatchedMask == 8);
            Require(e.Apply(default, Click(interval: 200), 176).Buttons == 0);
        });
        Check("Reset discards every click latch", () =>
        {
            var e = new TurboEngine(); var c = Click(); e.Apply(a, c, 0); e.Reset();
            Require(e.LatchedMask == 0 && e.Apply(default, c, 100).Buttons == 0);
        });
        Check("Legacy JSON defaults to hold mode and new per-button modes survive normalization", () =>
        {
            var legacy = JsonSerializer.Deserialize<AppSettings>("{\"Keys\":[{\"Enabled\":true,\"IntervalMs\":150}]}", Store.Json)!;
            legacy.Normalize(); Require(legacy.Keys.Length == 8 && legacy.Keys.All(k => !k.ToggleMode));
            legacy.Keys[0] = new(true, 250, true); legacy.Keys[7] = new(false, 400, true);
            var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(legacy, Store.Json), Store.Json)!;
            copy.Normalize(); Require(copy.Keys.SequenceEqual(legacy.Keys));
        });
        Check("Latched feedback stays visible after physical release", () =>
        {
            var model = new ButtonModel(3, new(true, 100, true));
            model.SetState(false, false, true, true); Require(model.State.Contains("再按停"));
            model.SetState(false, false, true); Require(model.State == "等待单击");
        });
        Check("Settings normalize malformed/out-of-range key arrays", () =>
        {
            var s = new AppSettings { Keys = [new(true, -7), new(false, 99999)], TriggerThreshold = 999, InputSlot = 8 };
            s.Normalize(); Require(s.Keys.Length == 8 && s.Keys[0].IntervalMs == 30 && s.Keys[1].IntervalMs == 5000 && s.TriggerThreshold == 254 && s.InputSlot == 3);
        });
        Check("Settings JSON round-trip keeps all eight independent intervals", () =>
        {
            var s = new AppSettings { Keys = Enumerable.Range(0, 8).Select(i => new KeySetting(i % 2 == 0, 30 + i * 137)).ToArray(), AutoStart = false };
            var r = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, Store.Json), Store.Json)!;
            Require(!r.AutoStart && r.Keys.SequenceEqual(s.Keys));
        });
        ControllerDevice Fixture(Guid cls, int count, string container = "one") => new(
            @"HID\VID_045E&PID_0B12\123", container, @"USB\VID_045E&PID_0B12\123", cls, count);
        var xusbClass = new Guid("d61ca365-5af4-4486-998b-9db4734c6ca3");
        Check("HidHide follows eligible single-device parent rule", () => Require(ControllerDevices.Paths([Fixture(xusbClass, 1)]).Length == 2));
        Check("HidHide never hides shared parent adapter", () => Require(ControllerDevices.Paths([Fixture(xusbClass, 2)]).Length == 1));
        Check("HidHide never hides parent of unrelated device class", () => Require(ControllerDevices.Paths([Fixture(new Guid("36fc9e60-c465-11cf-8056-444553540000"), 1)]).Length == 1));
        Check("Rejects ambiguous physical devices before modifying HidHide", () =>
        {
            bool rejected = false;
            try { ControllerDevices.Paths([Fixture(xusbClass, 1), Fixture(xusbClass, 1, "two")]); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected);
        });
        Check("Unicode device/application list round-trip preserves Chinese and emoji", () =>
        {
            var paths = new[] { "C:/\u6d4b\u8bd5/\u624b\u67c4/XboxRapidFire.exe", "HID/\u4e2d\u6587/\U0001f3ae" };
            var bytes = HidHideDriver.EncodeList(paths);
            Require(HidHideDriver.DecodeList(bytes, bytes.Length).SequenceEqual(paths));
        });
        Check("Empty Unicode driver lists round-trip", () =>
        {
            var bytes = HidHideDriver.EncodeList([]); Require(bytes.Length == 4 && HidHideDriver.DecodeList(bytes, bytes.Length).Length == 0);
        });
        Check("Invalid Unicode byte count is rejected", () =>
        {
            bool rejected = false;
            try { HidHideDriver.DecodeList(new byte[3], 3); } catch (IOException) { rejected = true; }
            Require(rejected);
        });
        Check("Language persists independently of controller settings and legacy JSON defaults to Chinese", () =>
        {
            var legacy = JsonSerializer.Deserialize<AppSettings>("{}", Store.Json)!; legacy.Normalize();
            Require(legacy.Language == "zh");
            var source = new AppSettings { Language = "en", Keys = Click().Keys };
            var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(source, Store.Json), Store.Json)!;
            copy.Normalize(); Require(copy.Language == "en" && copy.Keys.SequenceEqual(source.Keys));
        });
        Check("Bilingual button feedback and validation update without changing input mode", () =>
        {
            try
            {
                var model = new ButtonModel(3, new(true, 100, true)); int changes = 0;
                model.Changed += () => changes++;
                model.SetState(false, false, true, true); model.IntervalText = "29";
                Loc.SetLanguage("en"); model.RefreshLanguage();
                Require(model.Name == "A button" && model.ModeText == "Toggle" && model.State == "Firing · tap to stop");
                Require(model.Error == "Enter 30–5000 ms" && model.Enabled && model.ToggleMode && model.Interval == 100 && changes == 0);
                Require(Loc.T("未检测到 Xbox 手柄，请先连接手柄。").StartsWith("No Xbox controller"));
                Require(Loc.T("恢复手柄失败：未检测到 Xbox 手柄，请先连接手柄。") == "Controller recovery failed: No Xbox controller detected. Connect your controller first.");
                Loc.SetLanguage("zh"); model.RefreshLanguage();
                Require(model.State == "连发 · 再按停" && model.Error == "请输入 30–5000 ms" && changes == 0);
            }
            finally { Loc.SetLanguage("zh"); }
        });
        Store.Write(path, new { Passed = tests.Count - failed, Failed = failed, Tests = tests });
        return failed == 0 ? 0 : 1;
    }

    public static int Ui(string folder, bool testDriver = false)
    {
        Directory.CreateDirectory(folder);
        var tests = new List<object>(); int failed = 0;
        void Check(string name, bool ok) { tests.Add(new { Name = name, Passed = ok }); if (!ok) failed++; }
        var app = new System.Windows.Application();
        var window = new MainWindow(true);
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(350);
                Check("WPF window visible", window.IsVisible && window.ActualWidth >= 1000);
                Check("Custom application and tray icons load", window.TestHasCustomIcons);
                Check("Eight independent button models", window.Buttons.Length == 8 && window.ButtonCards.Items.Count == 8);
                var modeButtons = Enumerable.Range(0, 8).Select(i =>
                    (System.Windows.Controls.Button)((System.Windows.Controls.Grid)System.Windows.Media.VisualTreeHelper.GetParent(window.TestSwitch(i))).FindName("ModeButton")).ToArray();
                Check("Every card exposes its own mode button", modeButtons.All(b => b is not null && b.ActualWidth >= 44 && b.ActualHeight >= 44));
                Check("Mode button fits its row without clipped rounded edges", modeButtons.All(b => ((System.Windows.FrameworkElement)System.Windows.Media.VisualTreeHelper.GetParent(b)).ActualHeight >= b.ActualHeight));
                modeButtons[3].RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check("Mode click changes only selected card without enabling it", window.Buttons[3].ToggleMode && !window.Buttons[3].Enabled && window.Buttons.Where(b => b.Index != 3).All(b => !b.ToggleMode));
                Check("Mode label and accessible name reflect single-click behavior", modeButtons[3].Content.ToString() == "单击连发" && System.Windows.Automation.AutomationProperties.GetName(modeButtons[3]).Contains("单击连发"));
                modeButtons[3].RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check("Mode button cycles back to hold mode", !window.Buttons[3].ToggleMode && modeButtons[3].Content.ToString() == "长按连发");
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                    Check("Title bar, caption text and border override system accent", window.TestChromeColors);
                var toggle = window.TestSwitch(0);
                var thumb = (System.Windows.Shapes.Ellipse)toggle.Template.FindName("PART_Thumb", toggle);
                var slide = (System.Windows.Media.TranslateTransform)thumb.RenderTransform;
                Check("Toggle uses rounded keyboard focus without default dotted outline", toggle.FocusVisualStyle is not null && toggle.Template.FindName("Focus", toggle) is null);
                toggle.IsChecked = true;
                Check("Toggle applies setting immediately before animation ends", window.Buttons[0].Enabled);
                await Task.Delay(65);
                bool motion = System.Windows.SystemParameters.ClientAreaAnimation;
                Check("Toggle slides through intermediate positions or respects reduced motion", motion ? slide.X > 0 && slide.X < 24 : slide.X == 24);
                double beforeReverse = slide.X;
                toggle.IsChecked = false;
                await Task.Delay(20);
                Check("Rapid reversal continues from current position without jumping", !motion || slide.X > 0 && slide.X <= beforeReverse + 1);
                await Task.Delay(260);
                Check("Reversed toggle settles at left inset", slide.X == 0 && !window.Buttons[0].Enabled);
                toggle.IsChecked = true;
                await Task.Delay(260);
                Check("Enabled toggle settles at matching right inset", slide.X == 24);
                Check("Animating one toggle preserves the other seven settings", window.Buttons.Skip(1).All(b => !b.Enabled));
                toggle.IsChecked = false;
                await Task.Delay(260);
                await window.StartBridge(); Check("No selection prevents startup without driver changes", !window.Active && window.LastError is not null);
                var originalSettings = window.TestRuntime;
                window.EnglishButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await Task.Delay(60);
                Check("English selection updates window title and saved language immediately", window.Title == "Xbox Rapid Fire" && window.TestLanguage == "en");
                Check("English updates existing error and tray menu", window.StatusTitle.Text == "Unable to start" && window.StatusDetail.Text.StartsWith("Enable at least one") && window.TestTrayLabels.SequenceEqual(new[] { "Open window", "Pause / Resume rapid fire", "Exit and restore controller" }));
                Check("English updates bound modes, tooltips and screen-reader labels", window.Buttons[0].Name == "X button" && modeButtons[0].Content.ToString() == "Hold" && window.Buttons[0].AccessibleToggle == "X enable rapid fire" && window.Buttons[0].ModeHint.StartsWith("Hold the controller"));
                Check("Language switch leaves all button modes, intervals and pause state unchanged", originalSettings.Keys.SequenceEqual(window.TestRuntime.Keys) && originalSettings.Paused == window.TestRuntime.Paused);
                window.ChineseButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await Task.Delay(60);
                Check("Chinese selection restores existing error and tray labels", window.Title == "Xbox 连发助手" && window.TestLanguage == "zh" && window.StatusTitle.Text == "暂时无法启动" && window.TestTrayLabels[2] == "退出并恢复手柄");
                window.TestConfigure(3, true, 100); window.TestConfigure(7, true, 160);
                Check("Independent rate controls", window.Buttons[3].Interval == 100 && window.Buttons[7].Interval == 160);
                window.Buttons[3].IntervalText = "29";
                Check("Invalid rate retained with inline error", window.Buttons[3].Error is not null && window.Buttons[3].Interval == 100);
                window.Buttons[3].IntervalText = "100";
                Check("Valid edit clears error", window.Buttons[3].Error is null);
                window.Buttons[3].Step(-5000); Check("Minus clamps lower bound", window.Buttons[3].Interval == 30);
                window.Buttons[3].Step(99999); Check("Plus clamps upper bound", window.Buttons[3].Interval == 5000);
                window.TestConfigure(3, true, 100); window.TestReadyState();
                window.Buttons[3].ToggleMode = true;
                await Task.Delay(260);
                window.EnglishButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await Task.Delay(60);
                window.TestSnapshot(Path.Combine(folder, "window-en.png"));
                window.TestWindowSnapshot(Path.Combine(folder, "window-en-chrome.png"));
                window.Width = 1000; window.Height = 850; window.UpdateLayout();
                window.TestSnapshot(Path.Combine(folder, "window-en-min.png"));
                Check("English minimum window keeps language controls separate from connection panel", window.EnglishButton.TranslatePoint(new System.Windows.Point(0, window.EnglishButton.ActualHeight), window.Root).Y < window.InstallButton.TranslatePoint(new System.Windows.Point(0, 0), window.Root).Y - 65);
                window.Width = 1110; window.Height = 880;
                window.ChineseButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await Task.Delay(60);
                window.TestSnapshot(Path.Combine(folder, "window.png"));
                window.TestWindowSnapshot(Path.Combine(folder, "window-chrome.png"));
                Check("Screenshot captured", File.Exists(Path.Combine(folder, "window.png")));
                window.Width = 1000; window.Height = 850; window.UpdateLayout();
                window.TestSnapshot(Path.Combine(folder, "window-min.png"));
                Check("Minimum size renders", window.Root.ActualWidth > 900 && window.Root.ActualHeight > 700);
                Check("Mode buttons remain fully visible at minimum size", modeButtons.All(b => ((System.Windows.FrameworkElement)System.Windows.Media.VisualTreeHelper.GetParent(b)).ActualHeight >= b.ActualHeight));
                window.WindowState = System.Windows.WindowState.Minimized;
                await Task.Delay(100); Check("Minimize hides to tray", !window.IsVisible);
                window.Show(); window.WindowState = System.Windows.WindowState.Normal;
                Check("Window restores", window.IsVisible);
                window.Close(); await Task.Delay(100);
                Check("Close button hides window and keeps tray available", !window.IsVisible && window.TestTrayVisible);
                Check("Close preserves button settings", window.Buttons[3].Enabled && window.Buttons[3].Interval == 100 && window.Buttons[7].Enabled);
                Check("Close to tray preserves independent modes", window.Buttons[3].ToggleMode && !window.Buttons[7].ToggleMode);
                window.TestShowWindow();
                Check("Tray open restores a closed-to-tray window", window.IsVisible && window.WindowState == System.Windows.WindowState.Normal);
                window.Close(); await Task.Delay(100);
                Check("Repeated close continues to hide to tray", !window.IsVisible && window.TestTrayVisible);
                if (testDriver)
                {
                    window.TestShowWindow();
                    await window.StartBridge();
                    Check("Real controller starts through UI", window.Active && window.LastError is null);
                    if (!window.Active) throw new IOException(window.LastError ?? "Controller did not start.");
                    window.Close(); await Task.Delay(150);
                    Check("Closing to tray keeps real controller bridge running", !window.IsVisible && window.Active);
                    window.TogglePause(); Check("Real bridge can pause from tray state", window.Paused && window.Active);
                    window.TogglePause(); Check("Real bridge can resume from tray state", !window.Paused && window.Active);
                }
            }
            catch (Exception ex) { tests.Add(new { Name = "UI exception", Passed = false, Error = ex.ToString() }); failed++; }
            finally { window.TestTrayExit(); }
        };
        app.Run(window);
        Check("Tray exit closes application and removes tray icon", !window.IsVisible && !window.TestTrayVisible);
        Store.Write(Path.Combine(folder, "ui-tests.json"), new { Passed = tests.Count - failed, Failed = failed, Tests = tests });
        return failed == 0 ? 0 : 1;
    }
}
