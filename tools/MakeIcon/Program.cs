using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: MakeIcon <input.png> <output.ico>");
    return 1;
}

var pngPath = args[0];
var icoPath = args[1];
var sizes = new[] { 16, 32, 48, 64, 128, 256 };

using var src = Image.FromFile(pngPath);
var entries = new List<(byte w, byte h, byte[] png)>();

foreach (var size in sizes)
{
    using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, 0, 0, size, size);
    }

    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    byte w = size >= 256 ? (byte)0 : (byte)size;
    byte h = size >= 256 ? (byte)0 : (byte)size;
    entries.Add((w, h, ms.ToArray()));
}

using var fs = File.Create(icoPath);
using var bw = new BinaryWriter(fs);
bw.Write((ushort)0);
bw.Write((ushort)1);
bw.Write((ushort)entries.Count);
var offset = 6 + 16 * entries.Count;
foreach (var e in entries)
{
    bw.Write(e.w);
    bw.Write(e.h);
    bw.Write((byte)0);
    bw.Write((byte)0);
    bw.Write((ushort)1);
    bw.Write((ushort)32);
    bw.Write(e.png.Length);
    bw.Write(offset);
    offset += e.png.Length;
}
foreach (var e in entries)
    bw.Write(e.png);

Console.WriteLine($"Wrote {icoPath} ({new FileInfo(icoPath).Length} bytes)");
return 0;
