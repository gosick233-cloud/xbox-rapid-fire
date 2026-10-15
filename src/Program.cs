using System.Diagnostics;
using System.Text.Json;

namespace XboxRapidFire;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--watchdog")
        {
            Hiding.Watch(int.Parse(args[1]), long.Parse(args[2])); return 0;
        }
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--self-test") return SelfTest.Run(args[1]);
        if (args.Length == 2 && args[0] == "--native-probe") return IntegrationTest.NativeProbe(args[1]);
        if (args.Length == 2 && args[0] == "--hid-probe") return IntegrationTest.HidProbe(args[1]);
        if (args.Length == 2 && args[0] == "--virtual-test") return IntegrationTest.Virtual(args[1]);
        if (args.Length == 2 && args[0] == "--diagnose")
        {
            var diagnostic = new
            {
                Time = DateTimeOffset.Now, XInputSlots = XInput.Connected(), HidHideCli = Hiding.CliPath,
                ViGEmBusFile = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "ViGEmBus.sys")),
                RecoveryPending = File.Exists(Store.JournalPath), Runtime = Environment.Version.ToString()
            };
            Store.Write(args[1], diagnostic); return 0;
        }
        Loc.SetLanguage(Store.Load().Language);
        using var mutex = new Mutex(false, "Local\\XboxRapidFire.Instance");
        bool acquired;
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { MessageBox.Show(Loc.T("Xbox 连发助手已经在运行，请从右下角托盘打开。"), Loc.T("Xbox 连发助手")); return 0; }
        try
        {
            if (args.Length == 2 && args[0] == "--ui-test") return SelfTest.Ui(args[1]);
            if (args.Length == 2 && args[0] == "--ui-driver-test") return SelfTest.Ui(args[1], true);
            if (args.Length == 2 && args[0] == "--bridge-test") return IntegrationTest.Transaction(args[1]);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Store.Log(e.ExceptionObject.ToString()!);
            var app = new System.Windows.Application();
            app.DispatcherUnhandledException += (_, e) => { Store.Log(e.Exception.ToString()); MessageBox.Show(Loc.T(e.Exception.Message), Loc.T("Xbox 连发助手")); e.Handled = true; };
            app.Run(new MainWindow()); return 0;
        }
        finally { mutex.ReleaseMutex(); }
    }
}
