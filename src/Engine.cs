using System.Text.Json;

namespace XboxRapidFire;

internal readonly record struct KeySpec(string Id, string Label, ushort Mask);

internal static class Keys8
{
    public static readonly KeySpec[] All =
    [new("X", "X", 0x4000), new("Y", "Y", 0x8000), new("B", "B", 0x2000), new("A", "A", 0x1000),
     new("LB", "LB  左肩键", 0x0100), new("RB", "RB  右肩键", 0x0200),
     new("LT", "LT  左扳机", 0), new("RT", "RT  右扳机", 0)];

    public static bool Held(int i, Pad p, int threshold = 30) => i switch
    {
        6 => p.LeftTrigger > threshold,
        7 => p.RightTrigger > threshold,
        _ => (p.Buttons & All[i].Mask) != 0
    };
}

internal sealed record KeySetting(bool Enabled = false, int IntervalMs = 100, bool ToggleMode = false);
internal sealed record RuntimeSettings(KeySetting[] Keys, int TriggerThreshold = 30, bool Paused = false);
internal sealed class AppSettings
{
    public KeySetting[] Keys { get; set; } = Enumerable.Range(0, 8).Select(_ => new KeySetting()).ToArray();
    public bool AutoStart { get; set; } = true;
    public string Language { get; set; } = "zh";
    public int TriggerThreshold { get; set; } = 30;
    public int InputSlot { get; set; } = -1;

    public void Normalize()
    {
        Language = Language == "en" ? "en" : "zh";
        Keys = Enumerable.Range(0, 8).Select(i =>
        {
            var k = Keys is not null && i < Keys.Length ? Keys[i] : null;
            return new KeySetting(k?.Enabled ?? false, Math.Clamp(k?.IntervalMs ?? 100, 30, 5000), k?.ToggleMode ?? false);
        }).ToArray();
        TriggerThreshold = Math.Clamp(TriggerThreshold, 1, 254);
        InputSlot = Math.Clamp(InputSlot, -1, 3);
    }
}

internal static class Store
{
    public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XboxRapidFire");
    public static readonly string SettingsPath = Path.Combine(DirectoryPath, "settings.json");
    public static readonly string JournalPath = Path.Combine(DirectoryPath, "recovery.json");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static AppSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Json) ?? new();
            s.Normalize(); return s;
        }
        catch { return new(); }
    }
    public static void Write<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, Json));
        File.Move(temp, path, true);
    }
    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var file = Path.Combine(DirectoryPath, "app.log");
            if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024) File.Move(file, file + ".old", true);
            File.AppendAllText(file, $"{DateTimeOffset.Now:O} {message}\n");
        }
        catch { }
    }
}

// Each button owns its physical edge, latch and elapsed-time pulse phase.
// Unconfigured controls pass through; a toggle's cancel press is consumed.
internal sealed class TurboEngine
{
    private readonly double[] starts = Enumerable.Repeat(double.NaN, 8).ToArray();
    private readonly KeySetting?[] previous = new KeySetting?[8];
    private readonly bool[] wasHeld = new bool[8], latched = new bool[8];
    public byte LatchedMask { get; private set; }

    public Pad Apply(Pad input, RuntimeSettings config, double nowMs)
    {
        var output = input;
        LatchedMask = 0;
        for (int i = 0; i < 8; i++)
        {
            var key = config.Keys[i];
            bool held = Keys8.Held(i, input, config.TriggerThreshold);
            bool downEdge = held && !wasHeld[i];
            wasHeld[i] = held;
            bool changed = previous[i] is { } old && (old.Enabled != key.Enabled || old.ToggleMode != key.ToggleMode);
            bool rateChanged = previous[i]?.IntervalMs != key.IntervalMs;
            previous[i] = key;
            if (!key.Enabled || config.Paused)
            {
                latched[i] = false; starts[i] = double.NaN; continue;
            }
            if (changed)
            {
                latched[i] = false; starts[i] = double.NaN;
                downEdge = false; // A setting change is not a new physical press.
            }
            if (key.ToggleMode && downEdge)
            {
                latched[i] = !latched[i]; starts[i] = double.NaN;
            }
            bool active = key.ToggleMode ? latched[i] : held;
            if (!active)
            {
                starts[i] = double.NaN;
                if (key.ToggleMode) WriteButton(ref output, i, false, 0);
                continue;
            }
            if (key.ToggleMode) LatchedMask |= (byte)(1 << i);
            if (double.IsNaN(starts[i]) || rateChanged) starts[i] = nowMs;
            var interval = Math.Clamp(key.IntervalMs, 30, 5000);
            var on = ((nowMs - starts[i]) % interval) < interval / 2.0;
            // A latched trigger must cross game thresholds even when the activation edge is shallow.
            byte level = key.ToggleMode ? byte.MaxValue : i == 6 ? input.LeftTrigger : input.RightTrigger;
            WriteButton(ref output, i, on, level);
        }
        return output;
    }
    private static void WriteButton(ref Pad output, int i, bool on, byte level)
    {
        if (i == 6) output.LeftTrigger = on ? level : (byte)0;
        else if (i == 7) output.RightTrigger = on ? level : (byte)0;
        else if (on) output.Buttons |= Keys8.All[i].Mask;
        else output.Buttons &= (ushort)~Keys8.All[i].Mask;
    }
    public void Reset()
    {
        Array.Fill(starts, double.NaN); Array.Clear(previous);
        Array.Clear(wasHeld); Array.Clear(latched); LatchedMask = 0;
    }
}
