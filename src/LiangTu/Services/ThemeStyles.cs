using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.SolidColorBrush;

namespace LiangTu.Services;

public enum PinStyle
{
    Wood,
    Plastic,
    Metal,
    Vintage,
    FlatPlastic,
    FlatWood,
    FlatMetal,
    WidePlastic,
    WideWood,
    WideMetal
}

public enum PinShape
{
    Classic,
    Flat,  // 小夹子（宽底中孔）
    Wide   // 长尾夹
}

public enum RopeStyle
{
    Hemp,
    Cotton,
    Nylon,
    Wire
}

public enum PhotoStyle
{
    Card,
    Polaroid,
    Wood,
    Tape
}

public enum CharmStyle
{
    None,
    Bear,
    Sock,
    Cloud,
    Star,
    Heart,
    Moon
}

public readonly record struct PinPalette(
    MediaColor JawLeft,
    MediaColor JawRight,
    MediaColor JawStroke,
    MediaColor SpringOuter,
    MediaColor SpringInner,
    MediaColor Highlight,
    PinShape Shape = PinShape.Classic,
    MediaColor? Accent = null,
    MediaColor? Shade = null,
    MediaColor? Strip = null,
    MediaColor? Handle = null);

public readonly record struct RopePalette(
    MediaColor Stroke,
    MediaColor Shade,
    double Thickness,
    double ShadeThickness,
    MediaColor CapWood,
    MediaColor CapWoodDark,
    MediaColor CapKnot,
    MediaColor CapKnotLight);

public readonly record struct PhotoPalette(
    MediaColor Background,
    MediaColor Border,
    double BorderThickness,
    double CornerRadius,
    double Padding,
    double BottomPad,
    MediaColor NameForeground,
    bool ShowTape,
    MediaColor? TapeColor = null);

public readonly record struct PinGeometrySet(
    Geometry JawLeft,
    Geometry JawRight,
    Geometry Highlight,
    double CanvasWidth,
    double CanvasHeight,
    double FrameTopMargin,
    double SpringLeft,
    double SpringTop,
    double PivotLX,
    double PivotLY,
    double PivotRX,
    double PivotRY,
    double OpenAngle);

public static class ThemeStyles
{
    public static readonly (PinStyle Id, string Label)[] PinOptions =
    [
        (PinStyle.Wood, "原木"),
        (PinStyle.Plastic, "塑料（红蓝）"),
        (PinStyle.Metal, "金属银灰"),
        (PinStyle.Vintage, "旧木铜色"),
        (PinStyle.FlatPlastic, "小夹子（青）"),
        (PinStyle.FlatWood, "小夹子（木）"),
        (PinStyle.FlatMetal, "小夹子（银）"),
        (PinStyle.WidePlastic, "长尾夹（蓝）"),
        (PinStyle.WideWood, "长尾夹（木）"),
        (PinStyle.WideMetal, "长尾夹（银）"),
    ];

    public static readonly (RopeStyle Id, string Label)[] RopeOptions =
    [
        (RopeStyle.Hemp, "麻绳"),
        (RopeStyle.Cotton, "棉绳"),
        (RopeStyle.Nylon, "尼龙绳"),
        (RopeStyle.Wire, "金属丝"),
    ];

    public static readonly (PhotoStyle Id, string Label)[] PhotoOptions =
    [
        (PhotoStyle.Card, "默认白卡"),
        (PhotoStyle.Polaroid, "拍立得"),
        (PhotoStyle.Wood, "木框"),
        (PhotoStyle.Tape, "胶带贴"),
    ];

    /// <summary>Selectable charms (no None — clear via empty list / 「全部关闭」).</summary>
    public static readonly (CharmStyle Id, string Label)[] CharmOptions =
    [
        (CharmStyle.Bear, "小熊"),
        (CharmStyle.Sock, "小袜子"),
        (CharmStyle.Cloud, "云朵"),
        (CharmStyle.Star, "星星"),
        (CharmStyle.Heart, "爱心"),
        (CharmStyle.Moon, "月亮"),
    ];

    public static PinStyle ParsePin(string? s) =>
        Enum.TryParse<PinStyle>(s, true, out var v) ? v : PinStyle.Wood;

    public static RopeStyle ParseRope(string? s) =>
        Enum.TryParse<RopeStyle>(s, true, out var v) ? v : RopeStyle.Hemp;

    public static PhotoStyle ParsePhoto(string? s) =>
        Enum.TryParse<PhotoStyle>(s, true, out var v) ? v : PhotoStyle.Card;

    public static CharmStyle ParseCharm(string? s) =>
        Enum.TryParse<CharmStyle>(s, true, out var v) ? v : CharmStyle.None;

    /// <summary>Parse multi-select charms: "Bear,Sock" or legacy single "Bear" / "None".</summary>
    public static IReadOnlyList<CharmStyle> ParseCharms(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Equals("None", StringComparison.OrdinalIgnoreCase))
            return [];
        var list = new List<CharmStyle>();
        foreach (var part in s.Split([',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<CharmStyle>(part, true, out var v) || v == CharmStyle.None) continue;
            if (!list.Contains(v)) list.Add(v);
        }
        return list;
    }

    public static string FormatCharms(IEnumerable<CharmStyle> styles)
    {
        var parts = styles.Where(s => s != CharmStyle.None).Distinct().Select(s => s.ToString()).ToArray();
        return parts.Length == 0 ? "None" : string.Join(",", parts);
    }

    public static PhotoPalette Photo(PhotoStyle style) => style switch
    {
        PhotoStyle.Polaroid => new(
            Background: C(0xF8, 0xF6, 0xF0),
            Border: C(0xEE, 0xFF, 0xFF, 0xFF),
            BorderThickness: 1.2,
            CornerRadius: 2,
            Padding: 6,
            BottomPad: 18,
            NameForeground: C(0x66, 0x55, 0x44),
            ShowTape: false),
        PhotoStyle.Wood => new(
            Background: C(0xEE, 0xE0, 0xC8),
            Border: C(0xFF, 0x8B, 0x5E, 0x34),
            BorderThickness: 4.5,
            CornerRadius: 3,
            Padding: 5,
            BottomPad: 4,
            NameForeground: C(0x5A, 0x3A, 0x1A),
            ShowTape: false),
        PhotoStyle.Tape => new(
            Background: C(0xF5, 0xF5, 0xF5),
            Border: C(0x33, 0xFF, 0xFF, 0xFF),
            BorderThickness: 0.6,
            CornerRadius: 2,
            Padding: 2,
            BottomPad: 2,
            NameForeground: C(0x55, 0x55, 0x55),
            ShowTape: true,
            TapeColor: C(0xAA, 0xF0, 0xD8, 0x80)),
        _ => new( // Card (default)
            Background: C(0xEE, 0xF5, 0xF5, 0xF5),
            Border: C(0x99, 0xFF, 0xFF, 0xFF),
            BorderThickness: 1.5,
            CornerRadius: 6,
            Padding: 4,
            BottomPad: 4,
            NameForeground: C(0xDD, 0x33, 0x33, 0x33),
            ShowTape: false),
    };

    public static PinPalette Pin(PinStyle style) => style switch
    {
        PinStyle.Plastic => Classic(
            C(0xE8, 0x4A, 0x3C), C(0x3A, 0x7B, 0xD5), C(0x5A, 0x2A, 0x28),
            C(0xF0, 0xE0, 0x60), C(0xFF, 0xF0, 0xA0), C(0x55, 0xFF, 0xFF, 0xFF)),
        PinStyle.Metal => Classic(
            C(0xC0, 0xC6, 0xCC), C(0xA8, 0xB0, 0xB8), C(0x5A, 0x62, 0x6A),
            C(0x7A, 0x82, 0x8A), C(0xD0, 0xD6, 0xDC), C(0x66, 0xFF, 0xFF, 0xFF)),
        PinStyle.Vintage => Classic(
            C(0x6B, 0x4A, 0x2A), C(0x5A, 0x3C, 0x22), C(0x2E, 0x1C, 0x10),
            C(0xB0, 0x7A, 0x3A), C(0xD4, 0xA0, 0x5A), C(0x33, 0xFF, 0xFF, 0xFF)),
        // 小夹子 — 参考「小夹子.png」：青绿描边、中孔褐、底条米白
        PinStyle.FlatPlastic => Small(
            body: C(0x1A, 0x4A, 0x4E), stroke: C(0x0E, 0x2E, 0x32),
            hole: C(0x8B, 0x5A, 0x45), strip: C(0xE8, 0xDC, 0xC8), hi: C(0x44, 0xFF, 0xFF, 0xFF)),
        PinStyle.FlatWood => Small(
            body: C(0xC4, 0x92, 0x5A), stroke: C(0x6E, 0x45, 0x24),
            hole: C(0x7A, 0x4A, 0x2A), strip: C(0xF0, 0xE6, 0xD4), hi: C(0x55, 0xFF, 0xFF, 0xFF)),
        PinStyle.FlatMetal => Small(
            body: C(0xB0, 0xB8, 0xC0), stroke: C(0x4A, 0x52, 0x5A),
            hole: C(0x6A, 0x52, 0x48), strip: C(0xE8, 0xEC, 0xF0), hi: C(0x66, 0xFF, 0xFF, 0xFF)),
        // 长尾夹 — 参考「夹子.png」：蓝身、深灰钢丝柄
        PinStyle.WidePlastic => Binder(
            body: C(0x4B, 0xA8, 0xE8), shade: C(0x2E, 0x7A, 0xB8), stroke: C(0x2A, 0x2A, 0x2A),
            handle: C(0x4A, 0x4A, 0x4A), hi: C(0x88, 0xFF, 0xFF, 0xFF)),
        PinStyle.WideWood => Binder(
            body: C(0xD2, 0xA0, 0x6A), shade: C(0xA0, 0x72, 0x40), stroke: C(0x4A, 0x2E, 0x18),
            handle: C(0x5A, 0x5A, 0x5A), hi: C(0x66, 0xFF, 0xFF, 0xFF)),
        PinStyle.WideMetal => Binder(
            body: C(0xC8, 0xD0, 0xD8), shade: C(0x8A, 0x94, 0x9E), stroke: C(0x3A, 0x42, 0x4A),
            handle: C(0x5A, 0x62, 0x6A), hi: C(0x88, 0xFF, 0xFF, 0xFF)),
        _ => Classic( // Wood
            C(0xD2, 0xA0, 0x6A), C(0xC4, 0x92, 0x5A), C(0x8B, 0x5E, 0x34),
            C(0xB0, 0xB0, 0xB0), C(0xD8, 0xD8, 0xD8), C(0x40, 0xFF, 0xFF, 0xFF)),
    };

    private static PinPalette Classic(
        MediaColor jawL, MediaColor jawR, MediaColor stroke,
        MediaColor springO, MediaColor springI, MediaColor hi) =>
        new(jawL, jawR, stroke, springO, springI, hi, PinShape.Classic);

    private static PinPalette Small(
        MediaColor body, MediaColor stroke, MediaColor hole, MediaColor strip, MediaColor hi) =>
        new(body, body, stroke, hole, hole, hi, PinShape.Flat, Accent: hole, Strip: strip);

    private static PinPalette Binder(
        MediaColor body, MediaColor shade, MediaColor stroke, MediaColor handle, MediaColor hi) =>
        new(body, shade, stroke, handle, handle, hi, PinShape.Wide, Shade: shade, Handle: handle);

    public static PinGeometrySet PinGeometry(PinShape shape) => shape switch
    {
        PinShape.Flat => FlatGeometry,
        PinShape.Wide => WideGeometry,
        _ => ClassicGeometry
    };

    private static readonly PinGeometrySet ClassicGeometry = CreateClassic();
    private static readonly PinGeometrySet FlatGeometry = CreateFlat();
    private static readonly PinGeometrySet WideGeometry = CreateWide();

    private static readonly Geometry EmptyGeo = CreateEmpty();

    private static Geometry CreateEmpty()
    {
        var g = Geometry.Empty;
        return g;
    }

    private static PinGeometrySet CreateClassic() => new(
        JawLeft: ParseFrozen("M 4,2 L 8,2 L 9,16 L 7,26 L 4,26 L 5,16 Z"),
        JawRight: ParseFrozen("M 14,2 L 18,2 L 17,16 L 18,26 L 15,26 L 13,16 Z"),
        Highlight: ParseFrozen("M 5,3 L 7.5,3 L 8,14 L 6,14 Z"),
        CanvasWidth: 22,
        CanvasHeight: 28,
        FrameTopMargin: 18,
        SpringLeft: 7,
        SpringTop: 11,
        PivotLX: 6.5,
        PivotLY: 12,
        PivotRX: 15.5,
        PivotRY: 12,
        OpenAngle: 26);

    private static PinGeometrySet CreateFlat() => new(
        JawLeft: EmptyGeo,
        JawRight: EmptyGeo,
        Highlight: EmptyGeo,
        CanvasWidth: 28,
        CanvasHeight: 24,
        FrameTopMargin: 16,
        SpringLeft: 0,
        SpringTop: 0,
        PivotLX: 14,
        PivotLY: 10,
        PivotRX: 14,
        PivotRY: 10,
        OpenAngle: 12);

    private static PinGeometrySet CreateWide() => new(
        JawLeft: EmptyGeo,
        JawRight: EmptyGeo,
        Highlight: EmptyGeo,
        CanvasWidth: 36,
        CanvasHeight: 34,
        FrameTopMargin: 26,
        SpringLeft: 0,
        SpringTop: 0,
        PivotLX: 12,
        PivotLY: 16,
        PivotRX: 24,
        PivotRY: 16,
        OpenAngle: 28);

    private static System.Windows.Media.Geometry ParseFrozen(string data)
    {
        var g = System.Windows.Media.Geometry.Parse(data);
        if (g is null) return Geometry.Empty;
        g.Freeze();
        return g;
    }

    public static RopePalette Rope(RopeStyle style) => style switch
    {
        RopeStyle.Cotton => new(
            Stroke: C(0xE8, 0xE0, 0xD0),
            Shade: C(0x66, 0xA0, 0x90, 0x70),
            Thickness: 3.4,
            ShadeThickness: 1.5,
            CapWood: C(0xC8, 0xB8, 0xA0),
            CapWoodDark: C(0x8A, 0x78, 0x60),
            CapKnot: C(0xD8, 0xD0, 0xC0),
            CapKnotLight: C(0xF0, 0xEA, 0xDC)),
        RopeStyle.Nylon => new(
            Stroke: C(0x2E, 0x5A, 0x9E),
            Shade: C(0x66, 0x1A, 0x35, 0x60),
            Thickness: 3.0,
            ShadeThickness: 1.3,
            CapWood: C(0x4A, 0x6A, 0x9A),
            CapWoodDark: C(0x2A, 0x3A, 0x5A),
            CapKnot: C(0x3A, 0x6A, 0xB0),
            CapKnotLight: C(0x6A, 0x9A, 0xD0)),
        RopeStyle.Wire => new(
            Stroke: C(0xB0, 0xB8, 0xC0),
            Shade: C(0x55, 0x60, 0x68, 0x70),
            Thickness: 1.8,
            ShadeThickness: 0.9,
            CapWood: C(0x9A, 0xA0, 0xA8),
            CapWoodDark: C(0x5A, 0x60, 0x68),
            CapKnot: C(0xC0, 0xC6, 0xCC),
            CapKnotLight: C(0xE0, 0xE6, 0xEC)),
        _ => new( // Hemp
            Stroke: C(0xC4, 0xA0, 0x6A),
            Shade: C(0x80, 0x60, 0x4A, 0x28),
            Thickness: 3.2,
            ShadeThickness: 1.4,
            CapWood: C(0xB8, 0x89, 0x5A),
            CapWoodDark: C(0x6E, 0x45, 0x24),
            CapKnot: C(0xA6, 0x7C, 0x52),
            CapKnotLight: C(0xC4, 0x96, 0x6A)),
    };

    private static MediaColor C(byte r, byte g, byte b) => MediaColor.FromRgb(r, g, b);
    private static MediaColor C(byte a, byte r, byte g, byte b) => MediaColor.FromArgb(a, r, g, b);

    public static MediaBrush Brush(MediaColor c)
    {
        var b = new MediaBrush(c);
        b.Freeze();
        return b;
    }
}
