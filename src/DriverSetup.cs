using System.Diagnostics;

namespace XboxRapidFire;

internal static class DriverSetup
{
    public static bool BusInstalled => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "ViGEmBus.sys"));
    public static async Task Install()
    {
        var files = new[] { "ViGEmBus_1.22.0_x64_x86_arm64.exe", "HidHide_1.5.230_x64.exe" };
        foreach (var file in files)
        {
            if (file.StartsWith("ViGEm") && BusInstalled) continue;
            if (file.StartsWith("HidHide") && Hiding.CliPath is not null) continue;
            var path = Path.Combine(AppContext.BaseDirectory, "Drivers", file);
            if (!File.Exists(path)) throw new IOException("未找到安装包，请完整解压工具文件夹后再运行。");
            await Verify(path);
            using var p = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" })
                ?? throw new IOException("无法启动驱动安装程序。");
            await p.WaitForExitAsync();
            if (p.ExitCode is not (0 or 3010 or 1641)) throw new IOException($"安装未完成，返回码 {p.ExitCode}。");
        }
    }
    private static async Task Verify(string path)
    {
        var psi = new ProcessStartInfo("pwsh.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-NonInteractive"); psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("$s=Get-AuthenticodeSignature -LiteralPath $env:XR_INSTALLER; if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'Nefarius Software Solutions'){exit 7}");
        psi.Environment["XR_INSTALLER"] = path;
        using var p = Process.Start(psi) ?? throw new IOException("无法验证驱动签名。");
        var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(); await Task.WhenAll(stdout, stderr);
        if (p.ExitCode != 0) throw new IOException("驱动签名验证失败，未执行安装。" + stderr.Result);
    }
}
