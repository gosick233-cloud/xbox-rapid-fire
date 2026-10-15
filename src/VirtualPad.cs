using System.Diagnostics;
using Nefarius.ViGEm.Client.Targets;

namespace XboxRapidFire;

internal static class VirtualPad
{
    public static int WaitForIndex(IXbox360Controller target, int[] existingSlots)
    {
        // The driver's LED/player number can differ from XInput's actual slot.
        // Discover the newly enumerated slot, never use an existing physical slot.
        target.SubmitReport();
        var wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds < 5000)
        {
            var added = XInput.Connected().Except(existingSlots).ToArray();
            if (added.Length == 1) return added[0];
            if (added.Length > 1) throw new IOException("连接期间检测到其他手柄，请只连接一只实体手柄后重试。");
            Thread.Sleep(20);
        }
        throw new IOException("虚拟手柄连接超时，请重启电脑后重试。");
    }
}
