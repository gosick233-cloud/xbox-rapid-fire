using System.Reflection;
using System.Windows.Media.Imaging;

namespace XboxRapidFire;

internal static class AppIcons
{
    private static Stream Open() => Assembly.GetExecutingAssembly().GetManifestResourceStream("XboxRapidFire.AppIcon")
        ?? throw new IOException("Application icon resource is missing.");

    public static Icon TrayIcon()
    {
        using var stream = Open();
        using var source = new Icon(stream, SystemInformation.SmallIconSize);
        return (Icon)source.Clone();
    }

    public static BitmapFrame WindowIcon()
    {
        using var stream = Open();
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze(); return frame;
    }
}
