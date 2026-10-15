using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace XboxRapidFire;

// Official HidHide control-device protocol; UTF-16 MULTI_SZ throughout.
// https://docs.nefarius.at/projects/HidHide/API-Documentation/
internal sealed class HidHideDriver : IDisposable
{
    private readonly SafeFileHandle handle;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string file, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, uint inputLength, byte[]? output, uint outputLength, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string device, [Out] char[] target, uint max);

    public HidHideDriver()
    {
        for (int i = 0; ; i++)
        {
            handle = CreateFileW(@"\\.\HidHide", 0x80000000, 7, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
            if (!handle.IsInvalid) return;
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            if (i < 4 && error is 5 or 32) { Thread.Sleep(40); continue; }
            throw new IOException(error is 5 or 32
                ? "HidHide 正被其他程序占用，请关闭 HidHide 配置窗口后重试。"
                : "无法连接 HidHide 驱动，请确认安装完成并按提示重启电脑。", new Win32Exception(error));
        }
    }
    private uint Io(int function, byte[]? input, byte[]? output)
    {
        uint code = (32769u << 16) | (1u << 14) | ((uint)function << 2);
        if (!DeviceIoControl(handle, code, input, (uint)(input?.Length ?? 0), output, (uint)(output?.Length ?? 0), out uint count, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HidHide 驱动操作失败");
        return count;
    }
    internal static byte[] EncodeList(IEnumerable<string> list)
    {
        var values = list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Any(v => v.Contains('\0'))) throw new ArgumentException("Device paths cannot contain NUL.");
        return Encoding.Unicode.GetBytes(string.Join('\0', values) + "\0\0");
    }
    internal static string[] DecodeList(byte[] bytes, int length)
    {
        if (length < 0 || length > bytes.Length || length % 2 != 0) throw new IOException("HidHide 返回了无效的 Unicode 数据长度。");
        return Encoding.Unicode.GetString(bytes, 0, length).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
    private string[] GetList(int function)
    {
        uint size = Io(function, null, null);
        if (size > 1024 * 1024) throw new IOException("HidHide 返回的数据长度异常。");
        if (size == 0) return [];
        var buffer = new byte[size];
        uint count = Io(function, null, buffer);
        return DecodeList(buffer, checked((int)count));
    }
    private void SetList(int function, string[] list) => Io(function, EncodeList(list), null);
    private bool GetFlag(int function)
    {
        var buffer = new byte[1];
        if (Io(function, null, buffer) != 1) throw new IOException("HidHide 返回的状态长度异常。");
        return buffer[0] != 0;
    }
    public string[] Hidden { get => GetList(2050); set => SetList(2051, value); }
    public string[] Applications { get => GetList(2048); set => SetList(2049, value); }
    public bool Active { get => GetFlag(2052); set => Io(2053, [value ? (byte)1 : (byte)0], null); }
    public bool Inverse => GetFlag(2054);
    internal static string NativeAppPath(string path)
    {
        if (path.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)) return path;
        path = Path.GetFullPath(path);
        var drive = Path.GetPathRoot(path)?.TrimEnd('\\');
        if (drive is null || drive.Length != 2 || drive[1] != ':') throw new IOException("请将工具放在本机磁盘上运行。");
        var target = new char[32768];
        if (QueryDosDeviceW(drive, target, (uint)target.Length) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var device = new string(target).Split('\0')[0];
        return device + path[2..];
    }
    public void Dispose() => handle.Dispose();
}
