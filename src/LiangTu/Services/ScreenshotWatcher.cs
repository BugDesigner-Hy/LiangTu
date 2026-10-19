using System.IO;

namespace LiangTu.Services;

public sealed class ScreenshotWatcher : IDisposable
{
    private static readonly HashSet<string> ImageExt =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };

    private readonly AppSettings _settings;
    /// <summary>path -> last observed LastWriteTimeUtc ticks</summary>
    private readonly Dictionary<string, long> _known = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private FileSystemWatcher? _desktopWatcher;
    private System.Threading.Timer? _debounce;
    private bool _disposed;

    public event Action<string>? NewScreenshot;

    public ScreenshotWatcher(AppSettings settings)
    {
        _settings = settings;
    }

    public string Folder => _settings.InboxPath;

    public void Start()
    {
        StopWatchers();
        _known.Clear();
        Directory.CreateDirectory(_settings.InboxPath);
        SeedKnown(_settings.InboxPath);

        _watcher = CreateWatcher(_settings.InboxPath);

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!PathsEqual(desktop, _settings.InboxPath))
        {
            SeedKnown(desktop);
            _desktopWatcher = CreateWatcher(desktop);
        }

        AppLog.Info($"Watching inbox: {_settings.InboxPath}");
    }

    public void Restart() => Start();

    private FileSystemWatcher CreateWatcher(string path)
    {
        var w = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };
        w.Created += OnChanged;
        w.Changed += OnChanged;
        w.Renamed += (_, e) => ScheduleScan(e.FullPath);
        return w;
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => ScheduleScan(e.FullPath);

    private void ScheduleScan(string? hint = null)
    {
        _debounce?.Dispose();
        _debounce = new System.Threading.Timer(_ =>
        {
            try { Scan(hint); }
            catch (Exception ex) { AppLog.Error($"Watcher scan: {ex.Message}"); }
        }, null, 350, Timeout.Infinite);
    }

    private void Scan(string? hint)
    {
        if (_disposed) return;
        var candidates = new List<string>();

        CollectChanged(_settings.InboxPath, candidates);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!PathsEqual(desktop, _settings.InboxPath))
            CollectChanged(desktop, candidates);

        if (!string.IsNullOrEmpty(hint) && File.Exists(hint) && IsImage(hint) && IsNewOrUpdated(hint))
            candidates.Add(hint);

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(File.GetLastWriteTimeUtc))
        {
            if (!WaitReadable(path)) continue;
            if (!TryMarkSeen(path)) continue;
            AppLog.Info($"New screenshot: {path}");
            NewScreenshot?.Invoke(path);
        }
    }

    private void CollectChanged(string dir, List<string> into)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            if (!IsImage(f)) continue;
            if (IsNewOrUpdated(f)) into.Add(f);
        }
    }

    private bool IsNewOrUpdated(string path)
    {
        try
        {
            var ticks = File.GetLastWriteTimeUtc(path).Ticks;
            if (!_known.TryGetValue(path, out var prev)) return true;
            // Allow a small clock skew; require a real rewrite.
            return ticks > prev;
        }
        catch
        {
            return false;
        }
    }

    private bool TryMarkSeen(string path)
    {
        try
        {
            var ticks = File.GetLastWriteTimeUtc(path).Ticks;
            if (_known.TryGetValue(path, out var prev) && ticks <= prev)
                return false;
            _known[path] = ticks;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SeedKnown(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            if (!IsImage(f)) continue;
            try { _known[f] = File.GetLastWriteTimeUtc(f).Ticks; }
            catch { /* ignore */ }
        }
    }

    private static bool IsImage(string path) => ImageExt.Contains(Path.GetExtension(path));

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static bool WaitReadable(string path)
    {
        long lastLen = -1;
        for (var i = 0; i < 50; i++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length > 500 && fs.Length == lastLen) return true;
                lastLen = fs.Length;
            }
            catch
            {
                // still writing
            }
            Thread.Sleep(80);
        }
        return lastLen > 500;
    }

    private void StopWatchers()
    {
        _watcher?.Dispose();
        _watcher = null;
        _desktopWatcher?.Dispose();
        _desktopWatcher = null;
    }

    public void Dispose()
    {
        _disposed = true;
        _debounce?.Dispose();
        StopWatchers();
    }
}
