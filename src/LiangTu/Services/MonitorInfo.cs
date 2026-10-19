namespace LiangTu.Services;

public sealed class MonitorInfo
{
    public int Index { get; init; }
    public WpfRect Bounds { get; init; }
    public bool IsPrimary { get; init; }
    public string Label => IsPrimary
        ? $"主屏 {Index + 1}  ({Bounds.Width:0}×{Bounds.Height:0})"
        : $"副屏 {Index + 1}  ({Bounds.Width:0}×{Bounds.Height:0})";

    public bool Matches(WpfRect other, double tol = 8) =>
        Math.Abs(Bounds.Left - other.Left) < tol
        && Math.Abs(Bounds.Top - other.Top) < tol
        && Math.Abs(Bounds.Width - other.Width) < tol
        && Math.Abs(Bounds.Height - other.Height) < tol;
}
