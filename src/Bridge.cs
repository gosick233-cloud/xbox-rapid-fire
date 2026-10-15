using System.Diagnostics;
using System.Runtime.InteropServices;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;

namespace XboxRapidFire;

internal sealed class Bridge : IDisposable
{
    private ViGEmClient? client;
    private IXbox360Controller? target;
    private Thread? thread;
    private volatile bool stopping;
    private RuntimeSettings settings;
    private int inputSlot = -1;
    private readonly object snapshotLock = new();
    private Pad physical, output;
    private byte latchedMask;
    private volatile string? failure;
    public string? Failure => failure;
    public bool Running => thread?.IsAlive == true && !stopping;
    public int InputSlot => inputSlot;
    public int OutputSlot { get; private set; } = -1;
    public (Pad Input, Pad Output, byte LatchedMask) Snapshot { get { lock (snapshotLock) return (physical, output, latchedMask); } }
    public Bridge(RuntimeSettings initial) { settings = initial; }
    public void Update(RuntimeSettings next) => Volatile.Write(ref settings, next);

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);

    public void Start()
    {
        var slots = XInput.Connected();
        if (slots.Length != 1) throw new InvalidOperationException(slots.Length == 0
            ? "未检测到 Xbox 手柄，请先连接手柄。"
            : "请只连接一只 Xbox 手柄，并关闭其他虚拟手柄工具后重试。");
        inputSlot = slots[0];
        try
        {
            // Create driver client before hiding; driver failure must never hide input.
            client = new ViGEmClient();
            Hiding.Begin();
            if (!XInput.Read(inputSlot, out _)) throw new IOException("工具无法读取已隐藏的手柄，已恢复普通输入。");
            target = client.CreateXbox360Controller();
            target.AutoSubmitReport = false;
            target.Connect();
            OutputSlot = VirtualPad.WaitForIndex(target, slots);
            if (OutputSlot < 0 || OutputSlot > 3 || OutputSlot == inputSlot)
                throw new IOException("未能区分实体手柄和虚拟手柄，已取消启动。");
            target.FeedbackReceived += (_, e) =>
            {
                if (!stopping) XInput.Rumble(inputSlot, e.LargeMotor, e.SmallMotor);
            };
            stopping = false; failure = null;
            thread = new Thread(Loop) { IsBackground = true, Name = "Xbox rapid-fire input", Priority = ThreadPriority.AboveNormal };
            thread.Start();
            Store.Log($"Bridge started: physical slot {inputSlot}, virtual slot {OutputSlot}");
        }
        catch
        {
            Dispose(); throw;
        }
    }

    private void Send(Pad p)
    {
        target!.SetButtonsFull(p.Buttons);
        target.LeftTrigger = p.LeftTrigger; target.RightTrigger = p.RightTrigger;
        target.LeftThumbX = p.LeftX; target.LeftThumbY = p.LeftY;
        target.RightThumbX = p.RightX; target.RightThumbY = p.RightY;
        target.SubmitReport();
    }
    private void Loop()
    {
        var engine = new TurboEngine(); var clock = Stopwatch.StartNew();
        var timer = timeBeginPeriod(1) == 0;
        try
        {
            while (!stopping)
            {
                if (!XInput.Read(inputSlot, out var p)) throw new IOException("手柄已断开，连发已停止。重新连接后点击“开始连发”。");
                var result = engine.Apply(p, Volatile.Read(ref settings), clock.Elapsed.TotalMilliseconds);
                Send(result);
                lock (snapshotLock) { physical = p; output = result; latchedMask = engine.LatchedMask; }
                Thread.Sleep(2);
            }
        }
        catch (Exception ex) { failure = ex.Message; Store.Log(ex.ToString()); }
        finally
        {
            try { Send(default); } catch (Exception ex) { Store.Log("Neutral: " + ex.Message); }
            XInput.Rumble(inputSlot, 0, 0);
            if (timer) timeEndPeriod(1);
        }
    }
    public void Dispose()
    {
        stopping = true;
        if (thread is not null && !thread.Join(5000)) throw new TimeoutException("输入线程尚未停止，请稍后重试。");
        thread = null;
        Exception? error = null;
        try { Hiding.Recover(); } catch (Exception ex) { error = ex; }
        try { target?.Disconnect(); } catch (Exception ex) { error ??= ex; }
        try { (target as IDisposable)?.Dispose(); } catch (Exception ex) { error ??= ex; }
        target = null;
        client?.Dispose(); client = null;
        if (inputSlot >= 0) XInput.Rumble(inputSlot, 0, 0);
        if (error is not null) throw error;
    }
}
