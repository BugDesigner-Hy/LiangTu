using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using LiangTu.Native;
using LiangTu.Services;

namespace LiangTu.Models;

public sealed class Line : INotifyPropertyChanged
{
    private readonly AppSettings _settings;
    private Guid? _copiedId;
    private Guid? _draggingId;
    private Guid? _pressedId;
    private bool _revealed;
    private bool _photosFolded;
    private int _gust;

    public ObservableCollection<Pegged> Items { get; } = new();

    /// <summary>How many newest photos stay fully visible on the rope.</summary>
    public const int VisibleSlots = 8;

    /// <summary>Hard cap of photos kept on the line (visible + folded). Oldest are dropped beyond this.</summary>
    public int MaxStored { get; set; } = 48;

    /// <summary>Deprecated alias — prefer MaxStored. Kept so callers compiling against MaxItems still build.</summary>
    public int MaxItems
    {
        get => MaxStored;
        set => MaxStored = value;
    }

    public Guid? CopiedId
    {
        get => _copiedId;
        set { _copiedId = value; OnPropertyChanged(); }
    }

    public Guid? DraggingId
    {
        get => _draggingId;
        set { _draggingId = value; OnPropertyChanged(); }
    }

    public Guid? PressedId
    {
        get => _pressedId;
        set { _pressedId = value; OnPropertyChanged(); }
    }

    public bool Revealed
    {
        get => _revealed;
        set { _revealed = value; OnPropertyChanged(); }
    }

    /// <summary>All photos tucked into one stack; rope stays visible.</summary>
    public bool PhotosFolded
    {
        get => _photosFolded;
        private set
        {
            if (_photosFolded == value) return;
            _photosFolded = value;
            OnPropertyChanged();
            Changed?.Invoke();
        }
    }

    public int Gust
    {
        get => _gust;
        private set { _gust = value; OnPropertyChanged(); }
    }

    public bool BreezeSwayEnabled => _settings.BreezeSway;

    public int LiveCount => Items.Count(i => !i.Falling);
    /// <summary>True while a dismiss/fall animation still owns a peg (exclude from LiveCount but keep on rope).</summary>
    public bool HasFallingItems => Items.Any(i => i.Falling);
    /// <summary>No live photos and no in-flight fall — safe to show empty knot / compact chrome.</summary>
    public bool IsVisuallyEmpty => LiveCount == 0 && !HasFallingItems;

    public Dictionary<Guid, WpfRect> HitRects { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<Pegged>? ItemAdded;
    public event Action? Changed;
    /// <summary>Pin/rope/photo theme changed — overlay and cards should re-apply colors.</summary>
    public event Action? ThemeChanged;
    /// <summary>User-visible notice: (title, body).</summary>
    public event Action<string, string>? Notice;

    public PinStyle CurrentPinStyle => ThemeStyles.ParsePin(_settings.PinStyle);
    public RopeStyle CurrentRopeStyle => ThemeStyles.ParseRope(_settings.RopeStyle);
    public PhotoStyle CurrentPhotoStyle => ThemeStyles.ParsePhoto(_settings.PhotoStyle);
    public IReadOnlyList<CharmStyle> CurrentCharmStyles => ThemeStyles.ParseCharms(_settings.CharmStyle);
    public bool HasCharms => CurrentCharmStyles.Count > 0;

    public Line(AppSettings settings)
    {
        _settings = settings;
        Restore();
        ScheduleGust();
    }

    public Guid? Hang(string path, bool quietly = false)
    {
        var full = Path.GetFullPath(path);
        Guid? resultId = null;

        // BitmapImage must be created on the WPF STA thread (IPC arrives on MTA).
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var thumb = MakeThumbnail(full);
            if (thumb is null)
            {
                // Tipray often finishes encrypting a moment later — caller may retry.
                // Do not toast here; false "无法预览" is worse than silent retry.
                AppLog.Warn($"Hang: thumbnail failed for {full}");
                return;
            }

            // Same path rewritten (e.g. Tipray overwrite) → replace existing card.
            var existing = Items.FirstOrDefault(i =>
                !i.Falling && string.Equals(i.Path, full, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.Thumb = thumb;
                Items.Remove(existing);
                Items.Add(existing);
                Save();
                Changed?.Invoke();
                resultId = existing.Id;
                AppLog.Info($"Hang: refreshed existing {full}");
                return;
            }

            var item = new Pegged(full, thumb);
            Items.Add(item);
            while (LiveCount > MaxStored)
            {
                // Prefer dropping unpinned oldest so pins survive longer.
                var oldest = Items.FirstOrDefault(i => !i.Falling && !i.Pinned)
                             ?? Items.FirstOrDefault(i => !i.Falling);
                if (oldest is null) break;
                Drop(oldest.Id, quietly: true);
            }

            // New shot should be visible on the rope.
            if (_photosFolded)
                _photosFolded = false;

            Save();
            ItemAdded?.Invoke(item);
            Changed?.Invoke();
            resultId = item.Id;
            AppLog.Info($"Hang: added {full}");
        });

        if (!quietly && resultId is not null && _settings.SoundOn) PlaySystemSound();
        return resultId;
    }

    public void SetBreezeSway(bool enabled)
    {
        if (_settings.BreezeSway == enabled) return;
        _settings.BreezeSway = enabled;
        _settings.Save();
        OnPropertyChanged(nameof(BreezeSwayEnabled));
        AppLog.Info($"Breeze sway => {enabled}");
    }

    public void SetPinStyle(PinStyle style)
    {
        var name = style.ToString();
        if (string.Equals(_settings.PinStyle, name, StringComparison.OrdinalIgnoreCase)) return;
        _settings.PinStyle = name;
        _settings.Save();
        OnPropertyChanged(nameof(CurrentPinStyle));
        ThemeChanged?.Invoke();
        AppLog.Info($"Pin style => {name}");
    }

    public void SetRopeStyle(RopeStyle style)
    {
        var name = style.ToString();
        if (string.Equals(_settings.RopeStyle, name, StringComparison.OrdinalIgnoreCase)) return;
        _settings.RopeStyle = name;
        _settings.Save();
        OnPropertyChanged(nameof(CurrentRopeStyle));
        ThemeChanged?.Invoke();
        AppLog.Info($"Rope style => {name}");
    }

    public void SetPhotoStyle(PhotoStyle style)
    {
        var name = style.ToString();
        if (string.Equals(_settings.PhotoStyle, name, StringComparison.OrdinalIgnoreCase)) return;
        _settings.PhotoStyle = name;
        _settings.Save();
        OnPropertyChanged(nameof(CurrentPhotoStyle));
        ThemeChanged?.Invoke();
        AppLog.Info($"Photo style => {name}");
    }

    public void ToggleCharmStyle(CharmStyle style)
    {
        if (style == CharmStyle.None)
        {
            ClearCharms();
            return;
        }

        var list = CurrentCharmStyles.ToList();
        if (list.Contains(style)) list.Remove(style);
        else list.Add(style);
        var encoded = ThemeStyles.FormatCharms(list);
        if (string.Equals(_settings.CharmStyle, encoded, StringComparison.OrdinalIgnoreCase)) return;
        _settings.CharmStyle = encoded;
        _settings.Save();
        OnPropertyChanged(nameof(CurrentCharmStyles));
        OnPropertyChanged(nameof(HasCharms));
        ThemeChanged?.Invoke();
        AppLog.Info($"Charm styles => {encoded}");
    }

    public void ClearCharms()
    {
        if (string.Equals(_settings.CharmStyle, "None", StringComparison.OrdinalIgnoreCase)) return;
        _settings.CharmStyle = "None";
        _settings.Save();
        OnPropertyChanged(nameof(CurrentCharmStyles));
        OnPropertyChanged(nameof(HasCharms));
        ThemeChanged?.Invoke();
        AppLog.Info("Charm styles => None");
    }

    public void Drop(Guid id, bool quietly = false)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null || item.Falling) return;
        item.Falling = true;
        HitRects.Remove(id);
        Save();
        Changed?.Invoke();
        OnPropertyChanged(nameof(LiveCount));

        // Keep item until leaf-dismiss animation finishes (~860ms) + small buffer.
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(950)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Items.Remove(item);
            Changed?.Invoke();
        };
        timer.Start();
    }

    public void Clear()
    {
        _photosFolded = false;
        var live = Items.Where(i => !i.Falling).ToList();
        for (var n = 0; n < live.Count; n++)
        {
            var item = live[n];
            var delay = 60 * n;
            var t = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(delay)
            };
            var quiet = n > 0;
            t.Tick += (_, _) =>
            {
                t.Stop();
                Drop(item.Id, quietly: quiet);
            };
            t.Start();
        }
    }

    public void Prune()
    {
        foreach (var item in Items.Where(i => !i.Falling && !File.Exists(i.Path)).ToList())
            Drop(item.Id, quietly: true);
    }

    public void Copy(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null || !File.Exists(item.Path)) return;

        try
        {
            var data = new System.Windows.Forms.DataObject();
            System.Drawing.Bitmap? bmp = null;
            var files = new System.Collections.Specialized.StringCollection { item.Path };
            data.SetFileDropList(files);

            // Only put a bitmap when the on-disk file is already plaintext.
            // Never decode Tipray ciphertext into a clear clipboard image / PNG side channel.
            if (IsPlainImageFile(item.Path))
            {
                bmp = TryLoadDrawingBitmap(item.Path);
                if (bmp is not null)
                    data.SetImage(bmp);
            }

            if (!data.GetDataPresent(System.Windows.Forms.DataFormats.Bitmap)
                && !data.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop))
            {
                Notice?.Invoke("复制失败", "无法读取该截图。");
                return;
            }

            System.Windows.Forms.Clipboard.SetDataObject(data, true);
            bmp?.Dispose();

            CopiedId = id;
            var clear = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            clear.Tick += (_, _) =>
            {
                clear.Stop();
                if (CopiedId == id) CopiedId = null;
            };
            clear.Start();
            if (_settings.SoundOn) PlaySystemSound();
            AppLog.Info($"Copied {item.Path}");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Copy failed: {ex.Message}");
            Notice?.Invoke("复制失败", ex.Message);
        }
    }

    private static bool IsPlainImageFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var hdr = new byte[12];
            var n = fs.Read(hdr, 0, hdr.Length);
            if (n < 4) return false;
            return LooksLikeImage(hdr);
        }
        catch { return false; }
    }

    private static System.Drawing.Bitmap? TryLoadDrawingBitmap(string path)
    {
        try
        {
            if (!IsPlainImageFile(path)) return null;
            using var img = System.Drawing.Image.FromFile(path);
            return new System.Drawing.Bitmap(img);
        }
        catch { return null; }
    }

    /// <summary>Rename the file on disk and update the peg. Returns true if renamed.</summary>
    public bool Rename(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null || !File.Exists(item.Path)) return false;

        var dir = Path.GetDirectoryName(item.Path) ?? "";
        var oldName = Path.GetFileName(item.Path);
        var ext = Path.GetExtension(oldName);
        var stem = Path.GetFileNameWithoutExtension(oldName);

        var dlg = new Views.RenameDialog(stem);
        if (dlg.ShowDialog() != true) return false;

        var input = dlg.FileName;
        if (string.IsNullOrWhiteSpace(input)) return false;
        foreach (var c in Path.GetInvalidFileNameChars())
            input = input.Replace(c, '_');

        if (!input.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ext))
            input += ext;

        var dest = Path.Combine(dir, input);
        if (string.Equals(dest, item.Path, StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            if (File.Exists(dest))
            {
                System.Windows.MessageBox.Show("已存在同名文件。", "重命名", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            File.Move(item.Path, dest);
            item.Path = dest;
            Save();
            Changed?.Invoke();
            AppLog.Info($"Renamed => {dest}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Rename failed: {ex.Message}");
            System.Windows.MessageBox.Show($"重命名失败：{ex.Message}", "重命名", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    public void Open(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null || !File.Exists(item.Path)) return;

        try
        {
            // Open Tipray original via Shell / default .bmp association — no cleartext PNG export.
            if (ShellFile.TryOpenLikeExplorer(item.Path))
            {
                AppLog.Info($"Opened: {item.Path}");
                return;
            }

            AppLog.Error($"Open failed: no viewer for {item.Path}");
            Notice?.Invoke("打开失败",
                "无法打开该 BMP。请在 Windows「设置 → 应用 → 默认应用」里为 .bmp 指定「照片」或「画图」，或在资源管理器中双击试一次。");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Open failed: {ex.Message}");
            Notice?.Invoke("打开失败", ex.Message);
        }
    }

    public void Markup(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null || !File.Exists(item.Path)) return;

        try
        {
            if (ShellFile.TryOpenWithPaint(item.Path) || ShellFile.TryOpenLikeExplorer(item.Path))
            {
                AppLog.Info($"Markup {item.Path}");
                return;
            }

            AppLog.Error($"Markup failed: no viewer for {item.Path}");
            Notice?.Invoke("打开画图失败", Path.GetFileName(item.Path));
        }
        catch (Exception ex)
        {
            AppLog.Error($"Markup failed: {ex.Message}");
            Notice?.Invoke("打开画图失败", ex.Message);
        }
    }

    public void RevealInExplorer(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.Path}\"");
    }

    /// <summary>Move an overflow item to the front of the rope (newest / visible).</summary>
    public void BringToFront(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id && !i.Falling);
        if (item is null) return;
        Items.Remove(item);
        Items.Add(item);
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Place a peg at a left-to-right index among currently visible slots
    /// (0 = leftmost). Overflow items stay older than the visible window.
    /// </summary>
    public void MoveToVisibleIndex(Guid id, int visibleIndex)
    {
        var live = Items.Where(i => !i.Falling).ToList();
        var item = live.FirstOrDefault(i => i.Id == id);
        if (item is null) return;

        live.Remove(item);
        var visStart = Math.Max(0, live.Count - (VisibleSlots - 1));
        var maxVis = Math.Min(VisibleSlots - 1, live.Count);
        visibleIndex = Math.Clamp(visibleIndex, 0, maxVis);
        var insertAt = visStart + visibleIndex;
        insertAt = Math.Clamp(insertAt, 0, live.Count);
        live.Insert(insertAt, item);

        // Rebuild Items preserving any falling entries at the end.
        var falling = Items.Where(i => i.Falling).ToList();
        Items.Clear();
        foreach (var p in live) Items.Add(p);
        foreach (var p in falling) Items.Add(p);

        Save();
        Changed?.Invoke();
        AppLog.Info($"Reorder {Path.GetFileName(item.Path)} → visible[{visibleIndex}]");
    }

    public void ToggleFold() => SetFolded(!_photosFolded);

    public void SetFolded(bool folded)
    {
        if (folded && LiveCount == 0) return;
        PhotosFolded = folded;
        AppLog.Info(folded ? "Photos folded" : "Photos expanded");
    }

    public IReadOnlyList<Pegged> VisibleItems()
    {
        if (_photosFolded) return Array.Empty<Pegged>();
        var live = Items.Where(i => !i.Falling).ToList();
        if (live.Count <= VisibleSlots) return live;

        // Pinned always claim front slots; remaining filled by newest unpinned.
        var pinnedVis = live.Where(i => i.Pinned).Take(VisibleSlots).ToList();
        var remain = VisibleSlots - pinnedVis.Count;
        var unpinned = live.Where(i => !i.Pinned).ToList();
        var unpinnedVis = remain > 0
            ? unpinned.Skip(Math.Max(0, unpinned.Count - remain)).ToList()
            : [];
        // Left→right: pinned cluster, then newest unpinned (same “front” feel).
        return pinnedVis.Concat(unpinnedVis).ToList();
    }

    public IReadOnlyList<Pegged> OverflowItems()
    {
        var live = Items.Where(i => !i.Falling).ToList();
        if (_photosFolded) return live;
        var visible = VisibleItems();
        var visIds = visible.Select(i => i.Id).ToHashSet();
        return live.Where(i => !visIds.Contains(i.Id)).ToList();
    }

    public void SetPinned(Guid id, bool pinned)
    {
        var item = Items.FirstOrDefault(i => i.Id == id && !i.Falling);
        if (item is null || item.Pinned == pinned) return;
        item.Pinned = pinned;
        Save();
        Changed?.Invoke();
        AppLog.Info($"{(pinned ? "Pinned" : "Unpinned")} {Path.GetFileName(item.Path)}");
    }

    public IEnumerable<string> LivePaths() =>
        Items.Where(i => !i.Falling).Select(i => i.Path);

    public bool IsInInbox(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return false;
        var inbox = Path.GetFullPath(_settings.InboxPath).TrimEnd('\\') + "\\";
        return Path.GetFullPath(item.Path).StartsWith(inbox, StringComparison.OrdinalIgnoreCase);
    }

    public void Discard(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id && !i.Falling);
        if (item is null) return;

        // Always leave the rope immediately. Tipray can hang/block SHFileOperation,
        // so never wait on recycle before Drop.
        var path = item.Path;
        var recycle = IsInInbox(id);
        Drop(id, quietly: true);
        AppLog.Info($"Discarded from rope: {path}");

        if (!recycle) return;
        _ = System.Threading.Tasks.Task.Run(() => RecycleBin.TryRecycle(path));
    }

    public void Trash(Guid id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        var path = item.Path;
        Drop(id, quietly: true);
        _ = System.Threading.Tasks.Task.Run(() => RecycleBin.TryRecycle(path));
    }

    public void ReloadThumbnail(string path)
    {
        var item = Items.FirstOrDefault(i => !i.Falling && string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        var thumb = MakeThumbnail(path);
        if (thumb is null) return;
        item.Thumb = thumb;
        Changed?.Invoke();
    }

    private sealed class PegPersist
    {
        public string Path { get; set; } = "";
        public bool Pinned { get; set; }
    }

    private void Save()
    {
        try
        {
            var rows = Items.Where(i => !i.Falling)
                .Select(i => new PegPersist { Path = i.Path, Pinned = i.Pinned })
                .ToList();
            var file = Path.Combine(AppPaths.Root, "pegged.json");
            File.WriteAllText(file, JsonSerializer.Serialize(rows));
        }
        catch { /* ignore */ }
    }

    private void Restore()
    {
        try
        {
            var file = Path.Combine(AppPaths.Root, "pegged.json");
            if (!File.Exists(file)) return;
            var json = File.ReadAllText(file);

            // Migrate legacy string[] format.
            if (json.TrimStart().StartsWith('['))
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array
                    && doc.RootElement.GetArrayLength() > 0
                    && doc.RootElement[0].ValueKind == JsonValueKind.String)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var p = el.GetString();
                        if (!string.IsNullOrEmpty(p) && File.Exists(p))
                            Hang(p, quietly: true);
                    }
                    Save(); // rewrite as objects
                    return;
                }
            }

            var rows = JsonSerializer.Deserialize<List<PegPersist>>(json) ?? [];
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.Path) || !File.Exists(row.Path)) continue;
                var id = Hang(row.Path, quietly: true);
                if (id is not null && row.Pinned)
                    SetPinned(id.Value, true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Restore line: {ex.Message}");
        }
    }

    private void ScheduleGust()
    {
        // Occasional wind gust — each card reacts independently with its own delay/amp.
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Random.Shared.Next(9, 22))
        };
        timer.Tick += (_, _) =>
        {
            if (_settings.BreezeSway && LiveCount > 0 && DraggingId is null && !PhotosFolded)
                Gust++;
            timer.Interval = TimeSpan.FromSeconds(Random.Shared.Next(9, 22));
        };
        timer.Start();
    }

    /// <summary>Windows default "Asterisk" system sound (控制面板 → 声音 → 星号).</summary>
    private static void PlaySystemSound()
    {
        try { System.Media.SystemSounds.Asterisk.Play(); }
        catch { /* ignore */ }
    }

    public static BitmapImage? MakeThumbnail(string path, int maxPixels = 480)
    {
        byte[]? bytes = null;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex)
        {
            AppLog.Warn($"MakeThumbnail read failed: {path} ({ex.Message})");
            bytes = null;
        }

        var looksEncrypted = bytes is null || !LooksLikeImage(bytes);
        if (looksEncrypted)
            AppLog.Info($"MakeThumbnail: direct read opaque/encrypted for {Path.GetFileName(path)} (len={bytes?.Length ?? 0}) — trying Shell");

        if (bytes is not null && !looksEncrypted)
        {
            var fromWic = TryThumbnailWic(bytes, maxPixels);
            if (fromWic is not null) return fromWic;

            var fromGdi = TryThumbnailGdi(bytes, maxPixels);
            if (fromGdi is not null) return fromGdi;

            var fromWpf = TryThumbnailWpf(bytes, maxPixels);
            if (fromWpf is not null) return fromWpf;
        }

        // 蓝盾/Tipray: direct File.Read returns ciphertext; Explorer Shell still has a preview.
        var fromShell = TryThumbnailShell(path, maxPixels);
        if (fromShell is not null) return fromShell;

        AppLog.Warn($"MakeThumbnail failed for {path}");
        return null;
    }

    private static bool LooksLikeImage(byte[] bytes)
    {
        if (bytes.Length < 4) return false;
        // BMP
        if (bytes[0] == (byte)'B' && bytes[1] == (byte)'M') return true;
        // PNG
        if (bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G') return true;
        // JPEG
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return true;
        // GIF
        if (bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F') return true;
        // WEBP (RIFF....WEBP)
        if (bytes.Length >= 12 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            return true;
        return false;
    }

    private static BitmapImage? TryThumbnailShell(string path, int maxPixels)
    {
        try
        {
            var src = ShellImage.TryGetBitmap(path, maxPixels);
            if (src is null) return null;
            AppLog.Info($"Shell thumb ok: {Path.GetFileName(path)} {src.PixelWidth}x{src.PixelHeight}");
            return ToBitmapImage(src);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Shell thumb failed ({ex.Message})");
            return null;
        }
    }

    private static BitmapImage? TryThumbnailWic(byte[] bytes, int maxPixels)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(
                ms, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            var frame = decoder.Frames[0];

            var w = frame.PixelWidth;
            var h = frame.PixelHeight;
            if (w <= 0 || h <= 0) return null;

            BitmapSource source = frame;
            var scale = Math.Min(1.0, maxPixels / (double)Math.Max(w, h));
            if (scale < 1.0)
            {
                source = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
                source.Freeze();
            }

            return ToBitmapImage(source);
        }
        catch (Exception ex)
        {
            AppLog.Info($"WIC thumb skip ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private static BitmapImage? TryThumbnailWpf(byte[] bytes, int maxPixels)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
            bmp.StreamSource = ms;
            // DecodePixelWidth breaks many .bmp (Tipray) — only use for non-BMP signatures.
            if (bytes.Length < 2 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
                bmp.DecodePixelWidth = maxPixels;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            AppLog.Info($"WPF thumb skip ({ex.GetType().Name})");
            return null;
        }
    }

    private static BitmapImage? TryThumbnailGdi(byte[] bytes, int maxPixels)
    {
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            using var img = System.Drawing.Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: false);
            var w = img.Width;
            var h = img.Height;
            if (w <= 0 || h <= 0) return null;

            var scale = Math.Min(1.0, maxPixels / (double)Math.Max(w, h));
            var tw = Math.Max(1, (int)Math.Round(w * scale));
            var th = Math.Max(1, (int)Math.Round(h * scale));

            using var thumb = new System.Drawing.Bitmap(tw, th, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = System.Drawing.Graphics.FromImage(thumb))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, 0, 0, tw, th);
            }

            using var png = new MemoryStream();
            thumb.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            return BytesToBitmapImage(png.ToArray());
        }
        catch (Exception ex)
        {
            AppLog.Warn($"GDI thumb failed ({ex.Message})");
            return null;
        }
    }

    private static BitmapImage? ToBitmapImage(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return BytesToBitmapImage(ms.ToArray());
    }

    private static BitmapImage BytesToBitmapImage(byte[] pngBytes)
    {
        using var ms = new MemoryStream(pngBytes, writable: false);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
