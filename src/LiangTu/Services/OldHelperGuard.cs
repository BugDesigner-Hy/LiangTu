using System.Diagnostics;
using System.Management;
using System.Windows;

namespace LiangTu.Services;

/// <summary>
/// Detects / stops the legacy PowerShell「蓝盾截图助手」(AutoSave-LdScreen.ps1).
/// </summary>
public static class OldHelperGuard
{
    private const string ScriptNeedle = "AutoSave-LdScreen";
    private static readonly string StartupLnk = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "蓝盾截图助手.lnk");

    public static int FindRunningCount()
    {
        try
        {
            return EnumHelperPids().Count;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"OldHelper detect failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// If the old helper is running (or its startup shortcut exists), ask the user
    /// whether to stop it. Returns true if the helper is no longer a conflict.
    /// </summary>
    public static bool PromptAndStopIfNeeded()
    {
        var pids = SafeEnumPids();
        var hasStartup = File.Exists(StartupLnk);
        if (pids.Count == 0 && !hasStartup) return true;

        var detail = pids.Count > 0
            ? $"检测到旧版「蓝盾截图助手」正在运行（{pids.Count} 个进程）。\n两者都会自动点「另存为」，会互相抢对话框。"
            : "检测到旧版「蓝盾截图助手」仍设为开机自启。";

        var r = System.Windows.MessageBox.Show(
            detail + "\n\n是否现在停止旧助手并关闭其开机自启？\n（推荐：是）",
            "晾图",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (r != MessageBoxResult.Yes) return pids.Count == 0;

        Stop(pids);
        return FindRunningCount() == 0;
    }

    public static int StopAll()
    {
        var pids = SafeEnumPids();
        Stop(pids);
        return pids.Count;
    }

    private static void Stop(IReadOnlyList<int> pids)
    {
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
                AppLog.Info($"Stopped old helper pid={pid}");
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Stop old helper pid={pid}: {ex.Message}");
            }
        }

        try
        {
            if (File.Exists(StartupLnk))
            {
                File.Delete(StartupLnk);
                AppLog.Info("Removed old helper startup shortcut");
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Remove old helper startup: {ex.Message}");
        }
    }

    private static List<int> SafeEnumPids()
    {
        try { return EnumHelperPids(); }
        catch (Exception ex)
        {
            AppLog.Warn($"OldHelper enum failed: {ex.Message}");
            return [];
        }
    }

    private static List<int> EnumHelperPids()
    {
        var list = new List<int>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, Name, CommandLine FROM Win32_Process " +
            "WHERE Name = 'powershell.exe' OR Name = 'pwsh.exe' OR Name = 'powershell_ise.exe'");
        foreach (ManagementObject mo in searcher.Get())
        {
            var cmd = mo["CommandLine"] as string ?? "";
            if (cmd.IndexOf(ScriptNeedle, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (mo["ProcessId"] is uint id)
                list.Add((int)id);
            else if (mo["ProcessId"] is int i)
                list.Add(i);
        }
        return list;
    }
}
