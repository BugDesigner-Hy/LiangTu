using System.Windows.Media.Imaging;

namespace LiangTu.Models;

public sealed class Pegged
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Path { get; set; }
    public BitmapImage Thumb { get; set; }
    public double Tilt { get; }
    public bool Falling { get; set; }
    public bool Flying { get; set; }
    /// <summary>Stay in the front visible slots when newer shots arrive.</summary>
    public bool Pinned { get; set; }

    public Pegged(string path, BitmapImage thumb, bool pinned = false)
    {
        Path = path;
        Thumb = thumb;
        Pinned = pinned;
        Tilt = Random.Shared.NextDouble() * 5.0 - 2.5;
    }
}
