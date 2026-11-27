using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Numlock;

/// <summary>
/// 图标绘制。托盘图标是一枚键帽：实心 = NumLock 开启，描边 = 关闭，
/// 右下角绿点 = “保持常开”模式。
/// 本类只负责绘制，不探测系统状态（主题探测见 SystemTheme）。
/// </summary>
internal static class IconFactory
{
    private static readonly Color White = Color.FromArgb(255, 255, 255, 255);
    private static readonly Color NearBlack = Color.FromArgb(255, 28, 28, 28);
    private static readonly Color DotGreen = Color.FromArgb(255, 48, 209, 88);
    private static readonly Color TileBg = Color.FromArgb(255, 35, 38, 43);
    private static readonly Color TileBgTop = Color.FromArgb(255, 48, 52, 60);
    private static readonly Color TileBgBottom = Color.FromArgb(255, 24, 26, 31);
    private static readonly Color KeycapTop = Color.FromArgb(255, 255, 255, 255);
    private static readonly Color KeycapBottom = Color.FromArgb(255, 232, 235, 242);
    private static readonly Color BadgeRingDark = Color.FromArgb(255, 12, 12, 12);

    /// <summary>托盘键帽图标。</summary>
    public static Bitmap RenderTrayIcon(int size, bool numOn, bool whiteIcons, bool enforceBadge)
    {
        size = ClampSize(size);
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = NewGraphics(bmp);

        Color fg = whiteIcons ? White : NearBlack;
        float stroke = Math.Max(1.2f, size * 0.075f);
        float pad = size * 0.06f;
        var cap = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);

        using var capPath = Gfx.RoundedRect(cap, size * 0.20f);
        if (numOn)
        {
            using var fill = new SolidBrush(fg);
            g.FillPath(fill, capPath);
            DrawDigitOne(g, cap, whiteIcons ? TileBg : White, size * 0.56f); // 实心键帽上的数字取底色保证对比
        }
        else
        {
            using var pen = new Pen(fg, stroke);
            g.DrawPath(pen, capPath);
            DrawDigitOne(g, cap, fg, size * 0.56f);
        }

        if (enforceBadge)
        {
            float r = size * 0.205f;
            float margin = size * 0.04f;
            // 环统一用深色：深色任务栏下隔开白键帽与绿点；浅色任务栏下保证绿点在浅底上可辨识
            DrawEnforceBadge(g, size - r - margin, size - r - margin, r, BadgeRingDark, Math.Max(1f, size * 0.045f));
        }

        return bmp;
    }

    /// <summary>应用（exe）图标：macOS 26 Liquid Glass 深曜石圆角磁贴 + 顶缘高光反射 + 立体浮雕白色键帽
    /// （不含“保持常开”绿点——绿点在托盘图标里表示实时状态，印到应用图标上会被读成状态指示；应用图标只作恒定身份）。</summary>
    public static Bitmap RenderAppTile(int size)
    {
        size = ClampSize(size);
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = NewGraphics(bmp);

        var tileRect = new RectangleF(0, 0, size, size);
        using (var tile = Gfx.RoundedRect(tileRect, size * 0.225f))
        using (var bg = new LinearGradientBrush(new PointF(0, 0), new PointF(0, size), TileBgTop, TileBgBottom))
        {
            g.FillPath(bg, tile);

            // 大尺寸磁贴添加 macOS 26 玻璃顶缘微光反射与柔和顶部漫射
            if (size >= 24)
            {
                float inset = Math.Max(0.5f, size * 0.015f);
                var rimRect = new RectangleF(inset, inset, size - inset * 2f, size - inset * 2f);
                using var rimPath = Gfx.RoundedRect(rimRect, size * 0.22f);
                using var rimBrush = new LinearGradientBrush(
                    new PointF(0, 0),
                    new PointF(0, size),
                    Color.FromArgb(95, 255, 255, 255),
                    Color.FromArgb(14, 255, 255, 255));
                using var rimPen = new Pen(rimBrush, Math.Max(1f, size * 0.02f));
                g.DrawPath(rimPen, rimPath);
            }
        }

        float capPad = size * 0.205f;
        var cap = new RectangleF(capPad, capPad, size - capPad * 2f, size - capPad * 2f);

        // 键帽下方柔和投影（增强 macOS 26 物理键帽悬浮感）
        if (size >= 28)
        {
            var shadowRect = new RectangleF(cap.X, cap.Y + size * 0.025f, cap.Width, cap.Height);
            using var shadowPath = Gfx.RoundedRect(shadowRect, size * 0.12f);
            using var shadowBrush = new SolidBrush(Color.FromArgb(72, 0, 0, 0));
            g.FillPath(shadowBrush, shadowPath);
        }

        using (var capPath = Gfx.RoundedRect(cap, size * 0.12f))
        using (var fill = new LinearGradientBrush(new PointF(cap.X, cap.Y), new PointF(cap.X, cap.Bottom), KeycapTop, KeycapBottom))
        {
            g.FillPath(fill, capPath);
        }
        DrawDigitOne(g, cap, TileBgBottom, size * 0.365f);

        return bmp;
    }

    /// <summary>自检用：给定尺寸的应用图标位图（供“应用图标无绿点”断言）。</summary>
    public static Bitmap RenderAppTileForTest(int size) => RenderAppTile(size);

    /// <summary>把位图转成可长期持有的 Icon（顺手销毁 GDI 句柄）。</summary>
    public static Icon ToIconAndDestroyHandle(Bitmap bmp)
    {
        IntPtr h = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone();
        }
        finally
        {
            Native.DestroyIcon(h);
        }
    }

    /// <summary>右下角“保持常开”绿点徽标（托盘图标与应用磁贴共用）。</summary>
    private static void DrawEnforceBadge(Graphics g, float cx, float cy, float r, Color ringColor, float ringWidth)
    {
        var dot = new RectangleF(cx - r, cy - r, r * 2, r * 2);
        using var ring = new Pen(ringColor, ringWidth);
        using var fill = new SolidBrush(DotGreen);
        g.FillEllipse(fill, dot);
        g.DrawEllipse(ring, dot);
    }

    private static int ClampSize(int size) => Math.Clamp(size, 8, 1024);

    private static Graphics NewGraphics(Bitmap bmp)
    {
        var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return g;
    }

    /// <summary>在键帽内绘制数字“1”（按实测墨迹尺寸做视觉居中）。</summary>
    private static void DrawDigitOne(Graphics g, RectangleF cap, Color color, float targetDigitHeight)
    {
        // Segoe UI 数字墨迹高度 ≈ 0.70 em
        float em = targetDigitHeight / 0.70f;
        using var font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var fmt = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap,
        };
        var sz = g.MeasureString("1", font, PointF.Empty, fmt);
        float x = cap.X + (cap.Width - sz.Width) / 2f;
        float y = cap.Y + (cap.Height - sz.Height) / 2f;
        g.DrawString("1", font, brush, x, y, fmt);
    }
}

