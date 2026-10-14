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
    private static readonly Color DotGreen = Color.FromArgb(255, 46, 204, 113);
    private static readonly Color TileBg = Color.FromArgb(255, 35, 38, 43);
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

    /// <summary>应用（exe）图标：深色圆角磁贴 + 白色键帽（不含“保持常开”绿点——绿点在托盘图标里表示实时状态，
    /// 印到应用图标上会被读成状态指示；应用图标只作恒定身份）。</summary>
    public static Bitmap RenderAppTile(int size)
    {
        size = ClampSize(size);
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = NewGraphics(bmp);

        using (var tile = Gfx.RoundedRect(new RectangleF(0, 0, size, size), size * 0.22f))
        using (var bg = new SolidBrush(TileBg))
        {
            g.FillPath(bg, tile);
        }

        float capPad = size * 0.21f;
        var cap = new RectangleF(capPad, capPad, size - capPad * 2, size - capPad * 2);
        using (var capPath = Gfx.RoundedRect(cap, size * 0.115f))
        using (var fill = new SolidBrush(White))
        {
            g.FillPath(fill, capPath);
        }
        DrawDigitOne(g, cap, TileBg, size * 0.36f);

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

