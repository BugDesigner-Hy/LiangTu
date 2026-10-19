namespace LiangTu.Services;

/// <summary>
/// Prunes old screenshot files in the inbox that are not currently on the rope.
/// </summary>
public static class InboxCleanup
{
    private static readonly HashSet<string> ImageExt =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };

    /// <summary>Returns how many files were sent to the recycle bin.</summary>
    public static int Run(AppSettings settings, IEnumerable<string> keepPaths)
    {
        var inbox = settings.InboxPath;
        if (string.IsNullOrWhiteSpace(inbox) || !Directory.Exists(inbox))
            return 0;

        var days = settings.InboxRetentionDays;
        var maxFiles = settings.InboxMaxFiles;
        if (days <= 0 && maxFiles <= 0) return 0;

        var keep = new HashSet<string>(
            keepPaths.Select(p =>
            {
                try { return Path.GetFullPath(p); }
                catch { return p; }
            }),
            StringComparer.OrdinalIgnoreCase);

        List<FileInfo> files;
        try
        {
            files = Directory.EnumerateFiles(inbox)
                .Where(f => ImageExt.Contains(Path.GetExtension(f)))
                .Select(f => new FileInfo(f))
                .Where(f => f.Exists)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"InboxCleanup list failed: {ex.Message}");
            return 0;
        }

        var cutoff = days > 0 ? DateTime.UtcNow.AddDays(-days) : DateTime.MinValue;
        var toRemove = new List<FileInfo>();

        for (var i = 0; i < files.Count; i++)
        {
            var f = files[i];
            var full = f.FullName;
            if (keep.Contains(full)) continue;

            var overMax = maxFiles > 0 && i >= maxFiles;
            var tooOld = days > 0 && f.LastWriteTimeUtc < cutoff;
            if (overMax || tooOld)
                toRemove.Add(f);
        }

        var n = 0;
        foreach (var f in toRemove)
        {
            if (RecycleBin.TryRecycle(f.FullName)) n++;
        }

        if (n > 0) AppLog.Info($"InboxCleanup: recycled {n} file(s)");
        return n;
    }
}
