using System.Diagnostics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;

namespace XboxRapidFire;

internal static class IntegrationTest
{
    public static int HidProbe(string file)
    {
        try
        {
            var devices = ControllerDevices.Enumerate();
            using var driver = new HidHideDriver();
            Store.Write(file, new { Passed = true, Devices = devices, Paths = ControllerDevices.Paths(devices), driver.Hidden, driver.Applications, driver.Active, driver.Inverse }); return 0;
        }
        catch (Exception ex) { Store.Write(file, new { Passed = false, Error = ex.ToString() }); return 1; }
    }
    public static int NativeProbe(string file)
    {
        try
        {
            using var client = new ViGEmClient();
            Store.Write(file, new { NativeLibraryLoaded = true, BusConnected = true, Error = "" }); return 0;
        }
        catch (Exception ex)
        {
            bool loaded = ex.GetType().Name.Contains("VigemBusNotFound", StringComparison.OrdinalIgnoreCase);
            Store.Write(file, new { NativeLibraryLoaded = loaded, BusConnected = false, Error = ex.GetType().FullName + ": " + ex.Message });
            return loaded ? 0 : 1;
        }
    }
    private static void Submit(IXbox360Controller target, Pad p)
    {
        target.SetButtonsFull(p.Buttons);
        target.LeftTrigger = p.LeftTrigger; target.RightTrigger = p.RightTrigger;
        target.LeftThumbX = p.LeftX; target.LeftThumbY = p.LeftY;
        target.RightThumbX = p.RightX; target.RightThumbY = p.RightY;
        target.SubmitReport();
    }
    public static int Virtual(string file)
    {
        int count = 0, transitions = 0, clickSamples = 0; string? error = null;
        try
        {
            using var client = new ViGEmClient(); var target = client.CreateXbox360Controller();
            target.AutoSubmitReport = false;
            try
            {
                var existingSlots = XInput.Connected();
                target.Connect(); var slot = VirtualPad.WaitForIndex(target, existingSlots);
                var settings = new RuntimeSettings(Enumerable.Range(0, 8).Select(i => new KeySetting(true, 100 + i * 30)).ToArray());
                var engine = new TurboEngine(); var watch = Stopwatch.StartNew();
                var source = new Pad { Buttons = 0xF3F1, LeftTrigger = 147, RightTrigger = 237, LeftX = -17230, LeftY = 32000, RightX = -32768, RightY = 12345 };
                Pad previous = default;
                while (watch.ElapsedMilliseconds < 2000)
                {
                    var expected = engine.Apply(source, settings, watch.Elapsed.TotalMilliseconds);
                    Submit(target, expected);
                    bool matches = false; Pad last = default;
                    for (int i = 0; i < 20; i++)
                    {
                        if (XInput.Read(slot, out var observed) && observed.Equals(expected)) { matches = true; break; }
                        last = observed;
                        Thread.Sleep(1);
                    }
                    if (!matches) throw new IOException($"Virtual XInput readback differed: expected={Describe(expected)}, observed={Describe(last)}, slot={slot}.");
                    count++; if (!previous.Equals(expected)) transitions++; previous = expected;
                    Thread.Sleep(2);
                }
                engine.Reset();
                settings = new RuntimeSettings(Enumerable.Range(0, 8).Select(_ => new KeySetting(true, 100, true)).ToArray());
                var released = source; released.Buttons &= 0x0CFF; released.LeftTrigger = 0; released.RightTrigger = 0;
                var clickedOn = source; clickedOn.LeftTrigger = 255; clickedOn.RightTrigger = 255;
                foreach (var (time, pressed, on) in new[] { (0d, true, true), (25d, false, true), (50d, false, false), (100d, false, true), (125d, true, false), (150d, false, false), (200d, false, false) })
                {
                    var expected = on ? clickedOn : released;
                    var result = engine.Apply(pressed ? source : released, settings, time);
                    if (!result.Equals(expected)) throw new IOException($"Click-mode engine output mismatch at {time}ms.");
                    Submit(target, result);
                    bool matches = false;
                    for (int retry = 0; retry < 20; retry++)
                    {
                        if (XInput.Read(slot, out var observed) && observed.Equals(expected)) { matches = true; break; }
                        Thread.Sleep(1);
                    }
                    if (!matches) throw new IOException($"Click-mode virtual readback mismatch at {time}ms.");
                    clickSamples++;
                }
                Submit(target, default); Thread.Sleep(10);
                if (!XInput.Read(slot, out var neutral) || !neutral.Equals(default(Pad))) throw new IOException("Virtual neutral state mismatch.");
            }
            finally { target.Disconnect(); (target as IDisposable)?.Dispose(); }
        }
        catch (Exception ex) { error = ex.ToString(); }
        Store.Write(file, new { Passed = error is null, Samples = count, StateTransitions = transitions, ClickModeSamples = clickSamples, Error = error });
        return error is null ? 0 : 1;
    }
    private static string Describe(Pad p) => $"{p.Buttons:X4}/{p.LeftTrigger}/{p.RightTrigger}/{p.LeftX}/{p.LeftY}/{p.RightX}/{p.RightY}";
    public static int Transaction(string folder)
    {
        Directory.CreateDirectory(folder);
        using var bridge = new Bridge(new RuntimeSettings(Enumerable.Range(0, 8).Select(_ => new KeySetting(true, 100)).ToArray()));
        try
        {
            bridge.Start();
            Store.Write(Path.Combine(folder, "active.json"), new { Pid = Environment.ProcessId, InputSlot = bridge.InputSlot, OutputSlot = bridge.OutputSlot });
            Thread.Sleep(4000);
            if (bridge.Failure is not null) throw new IOException(bridge.Failure);
            bridge.Dispose();
            Store.Write(Path.Combine(folder, "result.json"), new { Passed = true, RecoveryPending = File.Exists(Store.JournalPath) });
            return 0;
        }
        catch (Exception ex)
        {
            Store.Write(Path.Combine(folder, "result.json"), new { Passed = false, Error = ex.ToString() }); return 1;
        }
    }
}
