using System.Runtime.InteropServices;

namespace XboxRapidFire;

[StructLayout(LayoutKind.Sequential)]
internal struct Pad
{
    public ushort Buttons;
    public byte LeftTrigger, RightTrigger;
    public short LeftX, LeftY, RightX, RightY;
}
[StructLayout(LayoutKind.Sequential)]
internal struct InputState { public uint Packet; public Pad Pad; }
[StructLayout(LayoutKind.Sequential)]
internal struct Vibration { public ushort Left, Right; }

internal static class XInput
{
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState(uint index, out InputState state);
    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    private static extern uint SetState(uint index, ref Vibration vibration);
    public static bool Read(int index, out Pad pad)
    {
        var ok = GetState((uint)index, out var state) == 0;
        pad = ok ? state.Pad : default; return ok;
    }
    public static int[] Connected() => Enumerable.Range(0, 4).Where(i => Read(i, out _)).ToArray();
    public static void Rumble(int index, byte left, byte right)
    {
        var v = new Vibration { Left = (ushort)(left * 257), Right = (ushort)(right * 257) };
        _ = SetState((uint)index, ref v);
    }
}
