using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AniSync.Ui;

/// <summary>Icônes de la zone de notification, dessinées à la volée : pastille verte = écoute active.</summary>
public static class TrayIcons
{
    static Icon? _on;
    static Icon? _off;

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    public static Icon Get(bool listening) => listening ? _on ??= Draw(true) : _off ??= Draw(false);

    /// <summary>Le logo AniSync, à n'importe quelle taille (aussi utilisé pour générer le .ico).</summary>
    public static Bitmap DrawLogo(int size, bool? status = null)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        float s = size;
        var rect = new RectangleF(s * 0.03f, s * 0.03f, s * 0.94f, s * 0.94f);
        using (var path = RoundedRect(rect, s * 0.24f))
        using (var fill = new LinearGradientBrush(rect, Color.FromArgb(0x4C, 0xC2, 0xFF), Color.FromArgb(0x1E, 0x7F, 0xC9), 45f))
            g.FillPath(fill, path);

        // Triangle « lecture »
        var play = new[]
        {
            new PointF(s * 0.36f, s * 0.26f),
            new PointF(s * 0.36f, s * 0.74f),
            new PointF(s * 0.76f, s * 0.50f),
        };
        using (var white = new SolidBrush(Color.White))
            g.FillPolygon(white, play);

        if (status is bool on)
        {
            float d = s * 0.46f;
            var dot = new RectangleF(s - d, s - d, d - 1, d - 1);
            using var border = new SolidBrush(Color.FromArgb(0x0B, 0x16, 0x22));
            using var color = new SolidBrush(on ? Color.FromArgb(0x3D, 0xDC, 0x84) : Color.FromArgb(0x8F, 0xA3, 0xB8));
            g.FillEllipse(border, dot);
            dot.Inflate(-s * 0.07f, -s * 0.07f);
            g.FillEllipse(color, dot);
        }
        return bmp;
    }

    static Icon Draw(bool listening)
    {
        using var bmp = DrawLogo(32, listening);
        var handle = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
