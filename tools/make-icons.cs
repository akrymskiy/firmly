#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property OutputType=Exe
#:property PublishAot=false

// Renders Firmly's app icon and MSIX logo assets from vector shapes.
//   dotnet run tools/make-icons.cs            -> writes src/Firmly.App/Assets
//   dotnet run tools/make-icons.cs -- <dir>   -> writes a preview sheet and assets to <dir> instead
//
// The icon is a battery charged to 80% (Battery Health Manager's cap) with pins on its sides, so it also
// reads as a firmware chip. Below 32 px the pins are dropped to keep the shape crisp.

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var outDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("src/Firmly.App/Assets");
var preview = args.Length > 0;

var thread = new Thread(() => Run(outDir, preview));
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

static void Run(string outDir, bool preview)
{
    Directory.CreateDirectory(outDir);

    WriteIco(Path.Combine(outDir, "AppIcon.ico"), [16, 20, 24, 32, 40, 48, 64, 128, 256]);

    // MSIX logos: canvas size, icon size.
    WritePng(Path.Combine(outDir, "Square44x44Logo.scale-200.png"), 88, 88, 84);
    WritePng(Path.Combine(outDir, "Square44x44Logo.targetsize-24_altform-unplated.png"), 24, 24, 24);
    WritePng(Path.Combine(outDir, "Square44x44Logo.targetsize-48_altform-lightunplated.png"), 48, 48, 48);
    WritePng(Path.Combine(outDir, "Square150x150Logo.scale-200.png"), 300, 300, 192);
    WritePng(Path.Combine(outDir, "Wide310x150Logo.scale-200.png"), 620, 300, 192);
    WritePng(Path.Combine(outDir, "SplashScreen.scale-200.png"), 1240, 600, 320);
    WritePng(Path.Combine(outDir, "LockScreenLogo.scale-200.png"), 48, 48, 48);
    WritePng(Path.Combine(outDir, "StoreLogo.png"), 50, 50, 50);

    if (preview)
        WritePreview(Path.Combine(outDir, "preview.png"));

    Console.WriteLine($"Wrote icons to {outDir}");
}

static void DrawIcon(DrawingContext dc, double x, double y, double size)
{
    var detailed = size >= 32;
    dc.PushTransform(new TranslateTransform(x, y));
    dc.PushTransform(new ScaleTransform(size / 256, size / 256));

    var metal = Gradient("#C9D3E6", "#8592AE");
    var body = Gradient("#46577D", "#222B41");
    var well = new SolidColorBrush(Hex("#121826"));
    var charge = Gradient("#5BEBAE", "#10A866");

    // Small sizes get a slightly wider body and no pins.
    var bodyRect = detailed ? new Rect(62, 46, 132, 190) : new Rect(52, 40, 152, 204);
    var radius = detailed ? 30 : 34;

    // Soft contact shadow.
    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(48, 0, 0, 0)), null,
        new Rect(bodyRect.X + 4, bodyRect.Y + 10, bodyRect.Width - 8, bodyRect.Height), radius, radius);

    if (detailed)
    {
        foreach (var py in new[] { 92.0, 140, 188 })
        {
            dc.DrawRoundedRectangle(metal, null, new Rect(40, py - 9, 30, 18), 6, 6);
            dc.DrawRoundedRectangle(metal, null, new Rect(186, py - 9, 30, 18), 6, 6);
        }
    }

    // Terminal.
    var terminalWidth = bodyRect.Width * 0.4;
    dc.DrawRoundedRectangle(metal, null,
        new Rect(128 - terminalWidth / 2, bodyRect.Y - 22, terminalWidth, 34), 10, 10);

    // Body with a light top edge.
    dc.DrawRoundedRectangle(body, new Pen(Gradient("#7F93BD", "#2A3550"), 3), bodyRect, radius, radius);

    // Window and the 80% charge.
    var inset = detailed ? 17 : 22;
    var wellRect = new Rect(bodyRect.X + inset, bodyRect.Y + inset, bodyRect.Width - 2 * inset, bodyRect.Height - 2 * inset);
    var wellRadius = radius - inset * 0.6;
    dc.DrawRoundedRectangle(well, null, wellRect, wellRadius, wellRadius);

    var fillHeight = wellRect.Height * 0.8;
    dc.PushClip(new RectangleGeometry(wellRect, wellRadius, wellRadius));
    dc.DrawRectangle(charge, null, new Rect(wellRect.X, wellRect.Bottom - fillHeight, wellRect.Width, fillHeight));
    // Bright edge at the top of the charge marks the cap.
    dc.DrawRectangle(new SolidColorBrush(Hex("#B8F7D9")), null,
        new Rect(wellRect.X, wellRect.Bottom - fillHeight, wellRect.Width, detailed ? 6 : 9));
    dc.Pop();

    dc.Pop();
    dc.Pop();
}

static void WritePng(string path, int width, int height, double iconSize)
{
    var bitmap = Render(width, height, dc => DrawIcon(dc, (width - iconSize) / 2, (height - iconSize) / 2, iconSize));
    using var file = File.Create(path);
    Encode(bitmap, file);
}

// ICO with PNG-compressed entries, which Windows Vista and later read at every size.
static void WriteIco(string path, int[] sizes)
{
    var images = sizes.Select(s =>
    {
        using var ms = new MemoryStream();
        Encode(Render(s, s, dc => DrawIcon(dc, 0, 0, s)), ms);
        return ms.ToArray();
    }).ToArray();

    using var file = File.Create(path);
    using var w = new BinaryWriter(file);
    w.Write((ushort)0);
    w.Write((ushort)1);
    w.Write((ushort)sizes.Length);
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(images[i].Length);
        w.Write(offset);
        offset += images[i].Length;
    }
    foreach (var image in images)
        w.Write(image);
}

static void WritePreview(string path)
{
    int[] sizes = [16, 24, 32, 48, 64, 128, 256];
    var width = sizes.Sum() + 24 * (sizes.Length + 1);
    var rowHeight = 256 + 48;
    var bitmap = Render(width, rowHeight * 2, dc =>
    {
        foreach (var (row, bg) in new[] { (0, "#F3F3F3"), (1, "#202020") })
        {
            dc.DrawRectangle(new SolidColorBrush(Hex(bg)), null, new Rect(0, row * rowHeight, width, rowHeight));
            double x = 24;
            foreach (var s in sizes)
            {
                // Render each size at its real pixel size, so small ones show true rendering.
                var small = Render(s, s, c => DrawIcon(c, 0, 0, s));
                dc.DrawImage(small, new Rect(x, row * rowHeight + 24 + (256 - s), s, s));
                x += s + 24;
            }
        }
    });
    using var file = File.Create(path);
    Encode(bitmap, file);
}

static BitmapSource Render(int width, int height, Action<DrawingContext> draw)
{
    var visual = new DrawingVisual();
    RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);
    using (var dc = visual.RenderOpen())
        draw(dc);
    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    bitmap.Freeze();
    return bitmap;
}

static void Encode(BitmapSource bitmap, Stream stream)
{
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    encoder.Save(stream);
}

static LinearGradientBrush Gradient(string top, string bottom) =>
    new(Hex(top), Hex(bottom), new Point(0, 0), new Point(0, 1));

static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
