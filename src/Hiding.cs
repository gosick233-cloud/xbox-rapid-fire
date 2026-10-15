using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace XboxRapidFire;

internal sealed class HideJournal
{
    public int ParentPid { get; set; }
    public long ParentStartTicks { get; set; }
    public string AppPath { get; set; } = "";
    public bool AddedApp { get; set; }
    public bool PreviousCloak { get; set; }
    public string[] PreviousHidden { get; set; } = [];
    public string[] AddedDevices { get; set; } = [];
}

internal static class Hiding
{
    private static readonly string[] CliPaths =
    [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe"),
     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions", "HidHide", "HidHideCLI.exe")];
    public static string? CliPath => CliPaths.FirstOrDefault(File.Exists);

    public static void Begin()
    {
        if (File.Exists(Store.JournalPath)) Recover();
        var devices = ControllerDevices.Paths(ControllerDevices.Enumerate());
        using var driver = new HidHideDriver();
        if (driver.Inverse)
            throw new InvalidOperationException("HidHide 当前启用了反向名单模式。请先在 HidHide 中关闭反向模式，再使用本工具。");
        var hidden = driver.Hidden;
        var app = Environment.ProcessPath!;
        var nativeApp = HidHideDriver.NativeAppPath(app);
        var current = Process.GetCurrentProcess();
        var journal = new HideJournal
        {
            ParentPid = current.Id, ParentStartTicks = current.StartTime.ToUniversalTime().Ticks,
            AppPath = app, AddedApp = !driver.Applications.Contains(nativeApp, StringComparer.OrdinalIgnoreCase),
            PreviousCloak = driver.Active, PreviousHidden = hidden,
            AddedDevices = devices.Except(hidden, StringComparer.OrdinalIgnoreCase).ToArray()
        };
        Store.Write(Store.JournalPath, journal);
        var info = new ProcessStartInfo(app) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--watchdog");
        info.ArgumentList.Add(journal.ParentPid.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add(journal.ParentStartTicks.ToString(CultureInfo.InvariantCulture));
        using var watchdog = Process.Start(info) ?? throw new IOException("无法启动异常退出恢复进程。");
        try
        {
            if (journal.AddedApp) driver.Applications = driver.Applications.Append(nativeApp).ToArray();
            driver.Hidden = hidden.Concat(journal.AddedDevices).ToArray();
            driver.Active = true;
            var actual = driver.Hidden;
            if (!devices.All(d => actual.Contains(d, StringComparer.OrdinalIgnoreCase)) || !driver.Active ||
                !driver.Applications.Contains(nativeApp, StringComparer.OrdinalIgnoreCase))
                throw new IOException("未能确认防重复输入设置，已取消启动。");
        }
        catch { driver.Dispose(); Recover(); throw; }
    }

    public static void Recover()
    {
        if (!File.Exists(Store.JournalPath)) return;
        var j = JsonSerializer.Deserialize<HideJournal>(File.ReadAllText(Store.JournalPath), Store.Json)
            ?? throw new IOException("恢复记录无法读取。");
        // Remove only this session's additions; preserve other mapping tools' entries.
        using var driver = new HidHideDriver();
        driver.Hidden = driver.Hidden.Except(j.AddedDevices, StringComparer.OrdinalIgnoreCase).ToArray();
        var now = driver.Hidden;
        var restoreCloak = !j.PreviousCloak && now.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(j.PreviousHidden);
        if (restoreCloak) driver.Active = false;
        var nativeApp = HidHideDriver.NativeAppPath(j.AppPath);
        if (j.AddedApp) driver.Applications = driver.Applications.Where(a => !StringComparer.OrdinalIgnoreCase.Equals(a, nativeApp)).ToArray();
        var verify = driver.Hidden;
        if (j.AddedDevices.Any(d => verify.Contains(d, StringComparer.OrdinalIgnoreCase)))
            throw new IOException("手柄恢复尚未完成。请点击“恢复手柄”重试。");
        if (restoreCloak && driver.Active)
            throw new IOException("普通手柄输入模式尚未恢复，请点击“恢复手柄”重试。");
        if (j.AddedApp && driver.Applications.Contains(nativeApp, StringComparer.OrdinalIgnoreCase))
            throw new IOException("临时应用名单尚未恢复，请点击“恢复手柄”重试。");
        File.Delete(Store.JournalPath);
        Store.Log("Physical controller restored.");
    }

    public static void Watch(int pid, long startTicks)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.StartTime.ToUniversalTime().Ticks == startTicks)
            {
                while (!p.WaitForExit(1000))
                {
                    if (!File.Exists(Store.JournalPath)) return;
                    var live = JsonSerializer.Deserialize<HideJournal>(File.ReadAllText(Store.JournalPath), Store.Json);
                    if (live?.ParentStartTicks != startTicks) return;
                }
            }
        }
        catch (ArgumentException) { }
        catch (Exception ex) { Store.Log("Watch: " + ex); }
        // Serialize with the next application instance, which may already be recovering.
        using var mutex = new Mutex(false, "Local\\XboxRapidFire.Instance");
        bool acquired;
        try { acquired = mutex.WaitOne(10000); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return;
        try
        {
            if (!File.Exists(Store.JournalPath)) return;
            var j = JsonSerializer.Deserialize<HideJournal>(File.ReadAllText(Store.JournalPath), Store.Json);
            if (j?.ParentStartTicks != startTicks) return;
            for (int i = 0; i < 5; i++)
            {
                try { Recover(); return; }
                catch (Exception ex) { Store.Log("Watch recovery: " + ex); Thread.Sleep(500); }
            }
        }
        finally { mutex.ReleaseMutex(); }
    }
}
