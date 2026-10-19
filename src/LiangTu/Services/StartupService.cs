namespace LiangTu.Services;

public static class StartupService
{
    private static string ShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "晾图.lnk");

    private static string CmdPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "晾图.cmd");

    public static bool IsEnabled => File.Exists(ShortcutPath) || File.Exists(CmdPath);

    public static void SetEnabled(bool enabled)
    {
        if (enabled) Enable();
        else Disable();
    }

    public static void Enable()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            AppLog.Warn("Cannot enable startup: ProcessPath missing");
            return;
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) throw new InvalidOperationException("WScript.Shell unavailable");
            dynamic shell = Activator.CreateInstance(shellType)!;
            var shortcut = shell.CreateShortcut(ShortcutPath);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
            shortcut.WindowStyle = 7;
            shortcut.Description = "晾图开机自启";
            shortcut.Save();
            if (File.Exists(CmdPath)) File.Delete(CmdPath);
            AppLog.Info($"Startup enabled: {ShortcutPath}");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Enable startup failed: {ex.Message}");
            File.WriteAllText(CmdPath, $"@echo off\r\nstart \"\" \"{exe}\"\r\n");
        }
    }

    public static void Disable()
    {
        try
        {
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            if (File.Exists(CmdPath)) File.Delete(CmdPath);
            AppLog.Info("Startup disabled");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Disable startup failed: {ex.Message}");
        }
    }
}
