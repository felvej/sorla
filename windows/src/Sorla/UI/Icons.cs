using System.Drawing.Drawing2D;

namespace Sorla.UI;

public enum TrayMark
{
    Ready,
    Busy,
    Recording,
    Problem,
}

// The Sorla mark from site/favicon.svg (a red dot and three bars on a dark rounded square), drawn at any size.
public static class Icons
{
    private static readonly Color Dark = Color.FromArgb(0x0B, 0x0B, 0x0C);
    private static readonly Color Red = Color.FromArgb(0xFF, 0x3B, 0x30);
    private static readonly Color Amber = Color.FromArgb(0xFF, 0xB0, 0x20);

    public static Bitmap Render(int size, TrayMark mark = TrayMark.Ready)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(size / 64f, size / 64f);

        using (var background = RoundedRect(new RectangleF(0, 0, 64, 64), 15))
        using (var brush = new SolidBrush(Dark))
        {
            g.FillPath(brush, background);
        }

        var dotColor = mark switch
        {
            TrayMark.Problem => Amber,
            TrayMark.Busy => Color.FromArgb(150, Red),
            _ => Red,
        };
        using (var dot = new SolidBrush(dotColor))
        {
            var radius = mark == TrayMark.Recording ? 8.5f : 6.5f;
            g.FillEllipse(dot, 19 - radius, 32 - radius, radius * 2, radius * 2);
        }

        var barAlpha = mark is TrayMark.Busy or TrayMark.Problem ? 110 : 255;
        using var bar = new SolidBrush(Color.FromArgb(barAlpha, Color.White));
        foreach (var (x, y, h) in new[] { (31f, 25f, 14f), (40f, 17f, 30f), (49f, 23f, 18f) })
        {
            using var path = RoundedRect(new RectangleF(x, y, 5, h), 2.5f);
            g.FillPath(bar, path);
        }
        return bitmap;
    }

    public static Icon TrayIcon(TrayMark mark)
    {
        using var bitmap = Render(Math.Max(16, SystemInformation.SmallIconSize.Width), mark);
        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            Platform.Native.DestroyIcon(handle);
        }
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
