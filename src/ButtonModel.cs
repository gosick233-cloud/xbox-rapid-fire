using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace XboxRapidFire;

internal sealed class ButtonModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;
    public int Index { get; }
    public string Id => Keys8.All[Index].Id;
    public string Name => Loc.T(Index switch { 0 => "X 按键", 1 => "Y 按键", 2 => "B 按键", 3 => "A 按键", 4 => "左肩键", 5 => "右肩键", 6 => "左扳机", _ => "右扳机" });
    public string AccessibleToggle => Loc.F("{0} 启用连发", Id);
    public string AccessibleInterval => Loc.F("{0} 连发周期毫秒，范围30到5000", Id);
    public string AccessibleMinus => Loc.F("{0} 周期减10毫秒", Id);
    public string AccessiblePlus => Loc.F("{0} 周期加10毫秒", Id);
    public string AccessibleMode => Loc.F("{0} 当前{1}，点击切换为{2}", Id, ModeText, Loc.T(ToggleMode ? "长按连发" : "单击连发"));
    public string ModeText => Loc.T(ToggleMode ? "单击连发" : "长按连发");
    public string ModeHint => Loc.T(ToggleMode ? "按一下手柄按钮开始持续连发，再按一下停止。点击切换为长按连发。" : "按住手柄按钮时连发，松开停止。点击切换为单击连发。");
    public string ToggleHint => Loc.T(ToggleMode ? "启用单击连发：按一次开始，再按一次停止。" : "启用长按连发：按住连发，松开停止。");
    public Brush BadgeBackground { get; }
    public Brush BadgeForeground { get; }
    private bool enabled;
    private bool toggleMode;
    private int interval;
    private string state = "普通输入";
    private bool held;
    public bool Enabled { get => enabled; set { if (enabled == value) return; enabled = value; Notify(); SetState(false, false, false); Changed?.Invoke(); } }
    public bool ToggleMode
    {
        get => toggleMode;
        set
        {
            if (toggleMode == value) return;
            toggleMode = value; Notify(); Notify(nameof(ModeText)); Notify(nameof(ModeHint));
            Notify(nameof(AccessibleMode)); Notify(nameof(ToggleHint));
            SetState(false, false, false); Changed?.Invoke();
        }
    }
    public int Interval => interval;
    public string IntervalText
    {
        get => interval.ToString();
        set
        {
            if (!int.TryParse(value, out int number) || number is < 30 or > 5000)
            {
                invalidInterval = true; Notify(nameof(RateText)); Notify(); return;
            }
            invalidInterval = false; interval = number; Notify(); Notify(nameof(RateText)); Changed?.Invoke();
        }
    }
    private bool invalidInterval;
    public string? Error => invalidInterval ? Loc.T("请输入 30–5000 ms") : null;
    public string RateText => Error ?? Loc.F("约 {0:0.#} 次 / 秒", 1000.0 / interval);
    public string State => Loc.T(state);
    public Brush StateBrush => held ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(165, 235, 131)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(164, 176, 173));
    public ButtonModel(int index, KeySetting setting)
    {
        Index = index; enabled = setting.Enabled; interval = setting.IntervalMs; toggleMode = setting.ToggleMode;
        var hex = index switch { 0 => "#94BEF4", 1 => "#E8CF87", 2 => "#EAA39B", 3 => "#A5EB83", _ => "#D8E3DA" };
        BadgeForeground = (Brush)new BrushConverter().ConvertFromString(hex)!;
        BadgeBackground = (Brush)new BrushConverter().ConvertFromString(index < 4 ? "#303D36" : "#303937")!;
        state = enabled ? "已启用" : "普通输入";
    }
    public void Step(int amount) { IntervalText = Math.Clamp(interval + amount, 30, 5000).ToString(); }
    public void RefreshLanguage() => Notify(string.Empty);
    public void SetState(bool pressed, bool sent, bool running, bool latched = false)
    {
        var next = !running ? (enabled ? "已启用" : "普通输入")
            : enabled && toggleMode ? (latched ? "连发 · 再按停" : "等待单击")
            : !pressed ? "等待按下" : enabled ? (sent ? "连发 · 按下" : "连发 · 间隔") : "普通按住";
        bool highlighted = pressed || latched;
        if (next == state && highlighted == held) return;
        state = next; held = highlighted; Notify(nameof(State)); Notify(nameof(StateBrush));
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
