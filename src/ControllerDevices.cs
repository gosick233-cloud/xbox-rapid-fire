using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace XboxRapidFire;

internal sealed record ControllerDevice(string Id, string Container, string BaseId, Guid BaseClass, int BaseChildCount);

// Enumerate HID interfaces with Unicode SetupAPI/Configuration Manager calls.
// Never parse localized console output or guess a truncated device identifier.
internal static class ControllerDevices
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfo { public uint Size; public Guid Class; public uint DevInst; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceInfo { public uint Size; public Guid Class; public uint Flags; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Format; public uint Id; public PropertyKey(string guid, uint id) { Format = new(guid); Id = id; } }
    [StructLayout(LayoutKind.Sequential)]
    private struct Attributes { public uint Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Caps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort Links, InputButtons, InputValues, InputData, OutputButtons, OutputValues, OutputData, FeatureButtons, FeatureValues, FeatureData;
    }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref Attributes attributes);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr list, IntPtr device, ref Guid guid, uint index, ref InterfaceInfo info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr list, ref InterfaceInfo info, IntPtr detail, uint length, out uint required, ref DeviceInfo device);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr list);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_IDW(uint node, StringBuilder id, uint length, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_DevNode_PropertyW(uint node, ref PropertyKey key, out uint type, byte[] value, ref uint length, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Child(out uint child, uint node, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Sibling(out uint sibling, uint node, uint flags);

    private static string Id(uint node)
    {
        var text = new StringBuilder(1024);
        if (CM_Get_Device_IDW(node, text, (uint)text.Capacity, 0) != 0) throw new IOException("手柄设备已断开，请重新连接后重试。");
        return text.ToString();
    }
    private static Guid GuidProperty(uint node, PropertyKey key)
    {
        var buffer = new byte[16]; uint length = 16;
        return CM_Get_DevNode_PropertyW(node, ref key, out uint type, buffer, ref length, 0) == 0 && type == 0x0D && length == 16 ? new Guid(buffer) : Guid.Empty;
    }
    private static readonly PropertyKey ContainerKey = new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c", 2);
    private static readonly PropertyKey ClassKey = new("a45c254e-df1c-4efd-8020-67d146a850e0", 10);
    private static ControllerDevice Describe(uint node)
    {
        var container = GuidProperty(node, ContainerKey);
        uint root = node;
        if (container != Guid.Empty && container != new Guid("00000000-0000-0000-ffff-ffffffffffff"))
            for (int i = 0; i < 64 && CM_Get_Parent(out uint parent, root, 0) == 0 && GuidProperty(parent, ContainerKey) == container; i++) root = parent;
        int count = 0;
        if (CM_Get_Child(out uint child, root, 0) == 0)
        {
            count = 1;
            while (count < 1024 && CM_Get_Sibling(out uint next, child, 0) == 0) { count++; child = next; }
        }
        string rootId = Id(root);
        return new(Id(node), container == Guid.Empty ? rootId : container.ToString(), rootId, GuidProperty(root, ClassKey), count);
    }
    public static ControllerDevice[] Enumerate()
    {
        HidD_GetHidGuid(out var guid);
        var list = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
        if (list == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var devices = new List<ControllerDevice>();
        try
        {
            for (uint i = 0; ; i++)
            {
                var info = new InterfaceInfo { Size = (uint)Marshal.SizeOf<InterfaceInfo>() };
                if (!SetupDiEnumDeviceInterfaces(list, IntPtr.Zero, ref guid, i, ref info))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                var node = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                _ = SetupDiGetDeviceInterfaceDetailW(list, ref info, IntPtr.Zero, 0, out uint size, ref node);
                if (size < 6 || size > 65536) throw new IOException("手柄接口路径长度异常。");
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(list, ref info, detail, size, out _, ref node)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4))!;
                    // Identification needs no input-report access and does not consume input.
                    using var hid = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (hid.IsInvalid) continue;
                    var attributes = new Attributes { Size = (uint)Marshal.SizeOf<Attributes>() };
                    if (!HidD_GetAttributes(hid, ref attributes) || attributes.Vendor != 0x045E) continue;
                    if (!HidD_GetPreparsedData(hid, out var data)) continue;
                    try
                    {
                        if (HidP_GetCaps(data, out var caps) == 0x00110000 &&
                            (caps.UsagePage == 5 || (caps.UsagePage == 1 && caps.Usage is 4 or 5))) devices.Add(Describe(node.DevInst));
                    }
                    finally { HidD_FreePreparsedData(data); }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(list); }
        return devices.DistinctBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static string[] Paths(ControllerDevice[] devices)
    {
        var groups = devices.GroupBy(d => d.Container, StringComparer.OrdinalIgnoreCase).ToArray();
        if (groups.Length != 1) throw new InvalidOperationException(groups.Length == 0
            ? "未找到 Xbox 手柄，请重新连接手柄后重试。"
            : "检测到多只 Xbox 手柄，请只连接要连发的这一只。");
        var members = groups[0].ToArray();
        var paths = members.Select(d => d.Id).ToList();
        var hid = new Guid("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
        var xusb = new Guid("d61ca365-5af4-4486-998b-9db4734c6ca3");
        foreach (var d in members)
            if ((d.BaseClass == hid || d.BaseClass == xusb) && d.BaseChildCount == members.Length) paths.Add(d.BaseId);
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
