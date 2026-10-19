using System.Diagnostics;
using System.Text;
using LiangTu.Native;

namespace LiangTu.Services;

/// <summary>
/// Ports the Tipray/蓝盾 Save-As automation from AutoSave-LdScreen.ps1.
/// </summary>
public sealed class SaveAsAutomator : IDisposable
{
    private static readonly string[] ProcessNames =
    [
        "LdScreen", "ldscreen", "LdClient", "ldbusiness_32", "ldbusiness"
    ];

    private readonly AppSettings _settings;
    private readonly System.Threading.Timer _timer;
    private readonly Dictionary<long, DateTime> _handled = new();
    private bool _disposed;

    public event Action? SaveConfirmed;
    /// <summary>Fired when the dialog was found but the filename/path could not be set.</summary>
    public event Action<string>? Warning;

    public SaveAsAutomator(AppSettings settings)
    {
        _settings = settings;
        _timer = new System.Threading.Timer(_ => Tick(), null, 250, 250);
    }

    private void Tick()
    {
        if (!_settings.AutomateSaveAs || _disposed) return;
        try
        {
            HandleDialogs();
        }
        catch (Exception ex)
        {
            AppLog.Error($"SaveAsAutomator: {ex.Message}");
        }
    }

    private void HandleDialogs()
    {
        foreach (var dlg in FindSaveAsDialogs())
        {
            var key = dlg.ToInt64();
            if (_handled.TryGetValue(key, out var when) && (DateTime.Now - when).TotalSeconds < 8)
                continue;

            var pn = GetProcessName(dlg);
            if (!ProcessNames.Any(a => a.Equals(pn, StringComparison.OrdinalIgnoreCase)))
                continue;

            var ldActive = Process.GetProcessesByName("LdScreen").Length > 0;
            if (!ldActive && !pn.Equals("LdScreen", StringComparison.OrdinalIgnoreCase))
                continue;

            var suggested = GetDialogFileName(dlg);
            // Keep Tipray's native encrypted BMP — do not force PNG (avoids cleartext re-encode).
            var uniqueName = $"晾图_{DateTime.Now:yyyyMMdd_HHmmss_fff}.bmp";
            AppLog.Info($"SaveAs hwnd={key} process={pn} suggested={suggested} => {uniqueName}");

            Win32.SetForegroundWindow(dlg);
            Thread.Sleep(80);

            // Prefer BMP / 位图 filter so Tipray encrypts as usual.
            TrySelectBmpFilter(dlg);
            Thread.Sleep(60);
            TrySelectBmpFilter(dlg);
            Thread.Sleep(40);

            var ok = false;
            if (_settings.ForceInboxPath)
            {
                Directory.CreateDirectory(_settings.InboxPath);
                var full = Path.Combine(_settings.InboxPath, uniqueName);
                ok = SetFileName(dlg, full);
                // Re-assert after filter change (dialog may rewrite extension to .bmp).
                Thread.Sleep(40);
                if (ok) SetFileName(dlg, full);
                AppLog.Info(ok ? $"set path: {full}" : "set path failed, fallback click-only");
            }
            else
            {
                ok = SetFileName(dlg, uniqueName);
                Thread.Sleep(40);
                if (ok) SetFileName(dlg, uniqueName);
                AppLog.Info(ok ? $"set unique name: {uniqueName}" : "unique name set failed, click-only");
            }

            if (!ok)
                Warning?.Invoke("未能写入另存为路径，已尝试直接点保存。若蓝盾界面有变，可暂时关闭「另存为写入收件箱」。");

            if (!ClickSave(dlg))
            {
                try { System.Windows.Forms.SendKeys.SendWait("{ENTER}"); }
                catch { /* ignore */ }
            }

            _handled[key] = DateTime.Now;
            SaveConfirmed?.Invoke();
            AppLog.Info("clicked save");
            return;
        }

        // prune old handled keys
        var stale = _handled.Where(kv => (DateTime.Now - kv.Value).TotalMinutes > 2).Select(kv => kv.Key).ToList();
        foreach (var k in stale) _handled.Remove(k);
    }

    private static List<IntPtr> FindSaveAsDialogs()
    {
        var list = new List<IntPtr>();
        Win32.EnumWindows((h, _) =>
        {
            if (!Win32.IsWindowVisible(h)) return true;
            var title = new StringBuilder(512);
            var cls = new StringBuilder(256);
            Win32.GetWindowText(h, title, 512);
            Win32.GetClassName(h, cls, 256);
            if (cls.ToString() != "#32770") return true;
            var t = title.ToString();
            if (t.Contains("另存", StringComparison.Ordinal) ||
                t.Contains("保存", StringComparison.Ordinal) ||
                t.Contains("Save As", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static string GetProcessName(IntPtr h)
    {
        try
        {
            Win32.GetWindowThreadProcessId(h, out var id);
            if (id == 0) return "";
            return Process.GetProcessById((int)id).ProcessName;
        }
        catch { return ""; }
    }

    private static IntPtr FindFileNameEdit(IntPtr dlg)
    {
        var edit = Win32.GetDlgItem(dlg, Win32.cmb13);
        if (edit == IntPtr.Zero) edit = Win32.GetDlgItem(dlg, Win32.edt1);
        if (edit != IntPtr.Zero) return edit;

        IntPtr found = IntPtr.Zero;
        Win32.EnumChildWindows(dlg, (ch, _) =>
        {
            var c = new StringBuilder(256);
            Win32.GetClassName(ch, c, 256);
            var cls = c.ToString();
            if ((cls == "Edit" || cls.Contains("Edit", StringComparison.Ordinal) || cls == "ComboBoxEx32")
                && found == IntPtr.Zero && Win32.IsWindowVisible(ch))
            {
                found = ch;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static string? GetDialogFileName(IntPtr dlg)
    {
        var edit = FindFileNameEdit(dlg);
        var text = GetEditText(edit);
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().Trim('"');
        try
        {
            if (text.Contains('\\') || text.Contains('/'))
                return Path.GetFileName(text);
        }
        catch { /* ignore */ }
        return text;
    }

    private static string GetEditText(IntPtr edit)
    {
        if (edit == IntPtr.Zero) return "";
        var len = Win32.SendMessage(edit, Win32.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero).ToInt32();
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 8);
        Win32.SendMessage(edit, Win32.WM_GETTEXT, (IntPtr)(len + 1), sb);
        return sb.ToString();
    }

    private static bool SetFileName(IntPtr dlg, string fullPath)
    {
        var edit = FindFileNameEdit(dlg);
        if (edit == IntPtr.Zero) return false;
        Win32.SendMessage(edit, Win32.WM_SETTEXT, IntPtr.Zero, fullPath);
        var got = GetEditText(edit);
        return got.Contains(Path.GetFileName(fullPath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Switch the Save-As "类型" combo to BMP/位图 when Tipray offers it.</summary>
    private static void TrySelectBmpFilter(IntPtr dlg)
    {
        try
        {
            var combo = Win32.GetDlgItem(dlg, Win32.cmb1);
            if (combo == IntPtr.Zero)
                combo = FindTypeCombo(dlg);
            if (combo == IntPtr.Zero) return;

            var idx = FindComboItem(combo, "BMP");
            if (idx < 0) idx = FindComboItem(combo, "*.bmp");
            if (idx < 0) idx = FindComboItem(combo, "位图");
            if (idx < 0) idx = FindComboItem(combo, "bmp");
            if (idx < 0) return;

            Win32.SendMessage(combo, Win32.CB_SETCURSEL, (IntPtr)idx, IntPtr.Zero);
            var wParam = (IntPtr)((Win32.CBN_SELENDOK << 16) | (Win32.cmb1 & 0xFFFF));
            Win32.SendMessage(dlg, Win32.WM_COMMAND, wParam, combo);
            AppLog.Info($"SaveAs type → BMP (index {idx})");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Select BMP filter: {ex.Message}");
        }
    }

    private static IntPtr FindTypeCombo(IntPtr dlg)
    {
        IntPtr found = IntPtr.Zero;
        Win32.EnumChildWindows(dlg, (ch, _) =>
        {
            if (!Win32.IsWindowVisible(ch)) return true;
            var c = new StringBuilder(256);
            Win32.GetClassName(ch, c, 256);
            var cls = c.ToString();
            if (cls is not ("ComboBox" or "ComboBoxEx32")) return true;

            // Skip the filename combo (cmb13); pick a combo that lists image types.
            var count = Win32.SendMessage(ch, Win32.CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (count < 2 || count > 40) return true;
            for (var i = 0; i < count; i++)
            {
                var text = GetComboItemText(ch, i);
                if (text.Contains("bmp", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("png", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("jpg", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("位图", StringComparison.Ordinal)
                    || text.Contains("图像", StringComparison.Ordinal))
                {
                    found = ch;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static int FindComboItem(IntPtr combo, string needle)
    {
        var count = Win32.SendMessage(combo, Win32.CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
        for (var i = 0; i < count; i++)
        {
            var text = GetComboItemText(combo, i);
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string GetComboItemText(IntPtr combo, int index)
    {
        var len = Win32.SendMessage(combo, Win32.CB_GETLBTEXTLEN, (IntPtr)index, IntPtr.Zero).ToInt32();
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 4);
        Win32.SendMessage(combo, Win32.CB_GETLBTEXT, (IntPtr)index, sb);
        return sb.ToString();
    }

    private static bool ClickSave(IntPtr dlg)
    {
        var btn = Win32.GetDlgItem(dlg, Win32.IDOK);
        if (btn != IntPtr.Zero)
        {
            Win32.SendMessage(btn, Win32.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
