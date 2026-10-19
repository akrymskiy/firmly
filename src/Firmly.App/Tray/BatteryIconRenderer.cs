namespace Firmly.App.Tray;

/// <summary>What the battery icon shows.</summary>
/// <param name="Percent">Charge, 0–100; null draws an empty battery.</param>
/// <param name="LimitPercent">Where the firmware stops charging, marked on the outline; null for none.</param>
internal sealed record BatteryIconState(int? Percent, BatteryIconStatus Status, int? LimitPercent);

internal enum BatteryIconStatus
{
    None,
    /// <summary>A lightning bolt.</summary>
    Charging,
    /// <summary>A plug: on power, not charging.</summary>
    PluggedIn,
    /// <summary>Red fill: running low on battery.</summary>
    Low,
}

/// <summary>
/// Draws the tray's battery icon at any size, as straight-alpha BGRA pixels. Plain C# with no graphics library:
/// each pixel is sampled 4×4 times against a few shapes designed on a 16×16 grid.
/// </summary>
internal static class BatteryIconRenderer
{
    private const int Samples = 4;

    // On a 16-unit grid: a horizontal battery with its terminal on the right, and room at the bottom right
    // for a badge (bolt or plug) that overlaps the battery's corner, as in Windows' own battery icon.
    // The terminal is thin, as in Windows' icon, so the body gets most of the width.
    private static readonly RoundedRect Body = new(0.5, 3.5, 14, 11.5, 2);
    private static readonly RoundedRect BodyInside = new(1.75, 4.75, 12.75, 10.25, 0.9);
    private static readonly RoundedRect Terminal = new(14, 6, 15, 9, 0.4);
    private const double FillLeft = 2.75, FillTop = 5.75, FillRight = 11.75, FillBottom = 9.25;
    private const double LimitTop = 1, LimitBottom = 3.5;

    private static readonly Polygon Bolt = new(
        [(13.25, 7.25), (9.25, 12.25), (11.75, 12.25), (10.75, 15.75), (15, 10.5), (12.5, 10.5)]);

    // A plug pointing up: two prongs, a body, and a cord.
    private static readonly Shape[] Plug =
    [
        new RoundedRect(10.25, 7.25, 11.25, 9.5, 0.3),
        new RoundedRect(12.75, 7.25, 13.75, 9.5, 0.3),
        new RoundedRect(9.25, 9.25, 14.75, 12.75, 1.25),
        new RoundedRect(11.5, 12.5, 12.5, 15.75, 0.3),
    ];

    // Room cleared around the badge so it reads over the outline and fill.
    private const double Halo = 1.0;

    /// <param name="lightTaskbar">Draw dark on light; otherwise light on dark.</param>
    /// <returns><paramref name="size"/>² pixels, row by row from the top, as 0xAARRGGBB.</returns>
    public static uint[] Render(BatteryIconState state, int size, bool lightTaskbar)
    {
        var foreground = lightTaskbar ? 0xFF1A1A1Au : 0xFFFFFFFFu;
        var low = lightTaskbar ? 0xFFC42B1Cu : 0xFFFF99A4u;
        var fillColor = state.Status == BatteryIconStatus.Low ? low : foreground;

        // A sliver stays visible down to 1%; nothing at 0.
        var percent = Math.Clamp(state.Percent ?? 0, 0, 100);
        RoundedRect? fill = percent == 0
            ? null
            : new(FillLeft, FillTop, Math.Max(FillLeft + (FillRight - FillLeft) * percent / 100.0, FillLeft + 0.75), FillBottom, 0.5);

        // A short notch above the battery where charging stops.
        Shape[] marks = [];
        if (state.LimitPercent is { } limit and > 0 and < 100)
        {
            var x = FillLeft + (FillRight - FillLeft) * limit / 100.0;
            marks = [new RoundedRect(x - 0.6, LimitTop, x + 0.6, LimitBottom, 0.3)];
        }

        Shape[] glyph = state.Status switch
        {
            BatteryIconStatus.Charging => [Bolt],
            BatteryIconStatus.PluggedIn => Plug,
            _ => [],
        };

        var pixels = new uint[size * size];
        var scale = 16.0 / size;
        for (var py = 0; py < size; py++)
        {
            for (var px = 0; px < size; px++)
            {
                double a = 0, r = 0, g = 0, b = 0;
                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var x = (px + (sx + 0.5) / Samples) * scale;
                        var y = (py + (sy + 0.5) / Samples) * scale;
                        var color = Sample(x, y, fill, fillColor, foreground, marks, glyph);
                        var alpha = (color >> 24) / 255.0;
                        a += alpha;
                        r += alpha * ((color >> 16) & 0xFF);
                        g += alpha * ((color >> 8) & 0xFF);
                        b += alpha * (color & 0xFF);
                    }
                }

                if (a > 0)
                {
                    var n = Samples * Samples;
                    pixels[py * size + px] =
                        (uint)Math.Round(a / n * 255) << 24
                        | (uint)Math.Round(r / a) << 16
                        | (uint)Math.Round(g / a) << 8
                        | (uint)Math.Round(b / a);
                }
            }
        }
        return pixels;
    }

    private static uint Sample(double x, double y, RoundedRect? fill, uint fillColor, uint foreground, Shape[] marks, Shape[] glyph)
    {
        if (glyph.Length > 0)
        {
            if (glyph.Any(s => s.Contains(x, y)))
                return foreground;
            if (glyph.Any(s => s.Distance(x, y) <= Halo))
                return 0;
        }

        if (fill?.Contains(x, y) == true)
            return fillColor;
        if ((Body.Contains(x, y) && !BodyInside.Contains(x, y)) || Terminal.Contains(x, y) || marks.Any(m => m.Contains(x, y)))
            return foreground;
        return 0;
    }

    private abstract class Shape
    {
        public abstract bool Contains(double x, double y);

        /// <summary>Distance from the shape's edge; 0 inside.</summary>
        public abstract double Distance(double x, double y);
    }

    private sealed class RoundedRect(double left, double top, double right, double bottom, double radius) : Shape
    {
        public override bool Contains(double x, double y) => Distance(x, y) <= 0;

        public override double Distance(double x, double y)
        {
            // Distance to the rectangle shrunk by the radius, minus the radius.
            var dx = Math.Max(Math.Max(left + radius - x, x - (right - radius)), 0);
            var dy = Math.Max(Math.Max(top + radius - y, y - (bottom - radius)), 0);
            return Math.Max(Math.Sqrt(dx * dx + dy * dy) - radius, 0);
        }
    }

    private sealed class Polygon((double X, double Y)[] points) : Shape
    {
        public override bool Contains(double x, double y)
        {
            var inside = false;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                var (xi, yi) = points[i];
                var (xj, yj) = points[j];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
            return inside;
        }

        public override double Distance(double x, double y)
        {
            if (Contains(x, y))
                return 0;

            var best = double.MaxValue;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                var (ax, ay) = points[j];
                var (bx, by) = points[i];
                var t = Math.Clamp(((x - ax) * (bx - ax) + (y - ay) * (by - ay)) / ((bx - ax) * (bx - ax) + (by - ay) * (by - ay)), 0, 1);
                var (cx, cy) = (ax + t * (bx - ax), ay + t * (by - ay));
                best = Math.Min(best, Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)));
            }
            return best;
        }
    }
}
