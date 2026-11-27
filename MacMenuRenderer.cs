using System.Drawing.Drawing2D;

namespace Numlock;

/// <summary>
/// macOS 风格托盘菜单几何指标（逻辑像素 × DPI 缩放）。
/// </summary>
internal readonly struct MenuMetrics
{
    public readonly float Scale;
    public readonly int MenuPad;
    public readonly int ItemPadV;
    public readonly int CheckSize;
    public readonly float HighlightRadius;
    public readonly float CornerRadius;

    private MenuMetrics(float scale, bool dwmCorners)
    {
        Scale = scale;
        MenuPad = Math.Max(2, (int)Math.Round(4 * scale));
        ItemPadV = (int)Math.Round(5 * scale);
        CheckSize = (int)Math.Round(11 * scale);
        HighlightRadius = Math.Max(2f, 5 * scale);
        CornerRadius = (dwmCorners ? 8f : 10f) * scale;
    }

    public static MenuMetrics For(int dpi, bool dwmCorners = true) => new(dpi / 96f, dwmCorners);
}

/// <summary>
/// macOS 26 (Tahoe) Liquid Glass 托盘菜单渲染器：
/// 1. 清空左侧灰色边栏槽（ImageMargin），采用纵向光学玻璃渐变底色与柔和顶部漫射；
/// 2. 悬停/按下态采用同心连续曲率液态玻璃胶囊（浅色 Apple 蓝光学渐变+顶缘高光；深色磨砂微光玻璃+高光边）；
/// 3. 统一全量菜单文本起始列至 X = 38px，与顶栏品牌名绝对垂直共线对齐；
/// 4. 子菜单左缘免绘外框描边，彻底消除父子菜单接缝双描边；
/// 5. 矢量圆角对勾、优雅内缩发丝分隔线与子菜单箭头，浅/深双模动态适配。
/// </summary>
internal sealed class MacMenuRenderer : ToolStripRenderer
{
    private bool _dark;

    public bool Dark
    {
        get => _dark;
        set => _dark = value;
    }

    private readonly Dictionary<ToolStrip, bool> _dwmCorners = new();

    public void SetDwmCorners(ToolStrip strip, bool rounded) => _dwmCorners[strip] = rounded;

    public bool CornersRounded(ToolStrip? strip) => strip == null || !_dwmCorners.TryGetValue(strip, out bool rounded) || rounded;

    private LiquidGlass.Palette Pal => LiquidGlass.Get(Dark);

    public Color MenuBack => Pal.MenuBgTop;
    public Color MenuBorder => Pal.OuterStroke;
    public Color MenuText => Pal.TextPrimary;
    public Color MenuHighlightBack => Dark ? Color.FromArgb(40, 255, 255, 255) : Pal.AccentSolid;
    public Color MenuPressedBack => Dark ? Color.FromArgb(58, 255, 255, 255) : Pal.AccentPressedTop;
    public Color MenuHighlightText => Color.White;
    public Color Dim => Pal.TextMuted;
    public Color Accent => Pal.AccentSolid;

    /// <summary>清空系统默认左侧灰色图像边栏，保证整个菜单玻璃底色一体通透。</summary>
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        var rect = new RectangleF(0, 0, e.ToolStrip.Width, e.ToolStrip.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var p = Pal;
        using var bg = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(0, rect.Height),
            p.MenuBgTop,
            p.MenuBgBottom);
        e.Graphics.FillRectangle(bg, rect);

        // 顶部柔和液态玻璃高光反射
        float sheenH = Math.Min(rect.Height * 0.32f, 84f);
        using var sheen = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(0, sheenH),
            Color.FromArgb(p.Dark ? 16 : 90, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255));
        e.Graphics.FillRectangle(sheen, 0, 0, rect.Width, sheenH);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        int w = e.ToolStrip.Width;
        int h = e.ToolStrip.Height;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        var p = Pal;
        bool isSubmenu = e.ToolStrip is ToolStripDropDown { OwnerItem: not null };

        // 子菜单左侧与父面板拼合，不绘左边框，确保接缝处最多 1px 单描边
        if (isSubmenu)
        {
            using var pen = new Pen(p.OuterStroke, 1f);
            e.Graphics.DrawLine(pen, 0, 0, w - 1, 0);         // top
            e.Graphics.DrawLine(pen, w - 1, 0, w - 1, h - 1); // right
            e.Graphics.DrawLine(pen, 0, h - 1, w - 1, h - 1); // bottom
            return;
        }

        // 主菜单绘制顶缘光学高光 + 细腻边框
        using var rimBrush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(0, h),
            p.SpecularTop,
            p.OuterStroke);
        using var borderPen = new Pen(rimBrush, 1f);
        e.Graphics.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is MacBrandHeaderItem header)
        {
            header.RenderCard(e.Graphics, e.ToolStrip);
            return;
        }

        if (!IsRowActive(e.Item) || !e.Item.Enabled)
        {
            return;
        }

        var p = Pal;
        float s = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        float padX = Math.Max(5f, 6f * s);
        float padY = Math.Max(1.5f, 1.5f * s);
        var rect = new RectangleF(padX, padY, e.Item.Width - padX * 2f, e.Item.Height - padY * 2f);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        // macOS 26 同心柔和圆角选中胶囊
        float radius = Math.Max(5.5f, 6.5f * s);
        using var path = Gfx.RoundedRect(rect, radius);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        if (Dark)
        {
            Color top = e.Item.Pressed ? Color.FromArgb(62, 255, 255, 255) : Color.FromArgb(46, 255, 255, 255);
            Color bot = e.Item.Pressed ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(26, 255, 255, 255);
            using var fill = new LinearGradientBrush(new PointF(rect.X, rect.Y), new PointF(rect.X, rect.Bottom), top, bot);
            e.Graphics.FillPath(fill, path);

            using var rimBrush = new LinearGradientBrush(
                new PointF(rect.X, rect.Y),
                new PointF(rect.X, rect.Bottom),
                Color.FromArgb(72, 255, 255, 255),
                Color.FromArgb(14, 255, 255, 255));
            using var rimPen = new Pen(rimBrush, 1f);
            e.Graphics.DrawPath(rimPen, path);
        }
        else
        {
            Color top = e.Item.Pressed ? p.AccentPressedTop : p.AccentTop;
            Color bot = e.Item.Pressed ? p.AccentPressedBottom : p.AccentBottom;
            using var fill = new LinearGradientBrush(new PointF(rect.X, rect.Y), new PointF(rect.X, rect.Bottom), top, bot);
            e.Graphics.FillPath(fill, path);

            // 浅色高亮蓝胶囊顶部微光折射边
            using var rimBrush = new LinearGradientBrush(
                new PointF(rect.X, rect.Y),
                new PointF(rect.X, rect.Bottom),
                Color.FromArgb(95, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255));
            using var rimPen = new Pen(rimBrush, 1f);
            e.Graphics.DrawPath(rimPen, path);
        }
    }

    private static bool IsRowActive(ToolStripItem item) =>
        item.Selected || item.Pressed || (item is ToolStripMenuItem mi && mi.DropDown.Visible);

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is MacBrandHeaderItem)
        {
            return;
        }

        float s = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        // 统一基准缩进列（38px 标尺）：与左侧对勾保持 11px 黄金间距，且与顶栏 "Numlock" 品牌名（38px）绝对垂直共线对齐
        float textX = Math.Max(34f, 38f * s);
        var textRect = new Rectangle(
            (int)Math.Round(textX),
            0,
            Math.Max(0, (int)Math.Round(e.Item.Width - textX - 12f * s)),
            e.Item.Height);

        bool active = IsRowActive(e.Item);
        Color textColor = !e.Item.Enabled
            ? Dim
            : active
                ? MenuHighlightText
                : MenuText;

        TextRenderer.DrawText(
            e.Graphics,
            e.Text,
            e.TextFont,
            textRect,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    /// <summary>勾选标记：矢量圆角对勾（距左 15px，选中态纯白反白，常规态 Apple 系统蓝）。</summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item is MacBrandHeaderItem)
        {
            return;
        }

        float s = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float checkW = Math.Max(10f, 12f * s);
        float checkH = Math.Max(8f, 10f * s);
        float x = Math.Max(13f, 15f * s);
        float y = (e.Item.Height - checkH) / 2f;

        bool active = IsRowActive(e.Item);
        Color checkColor = !e.Item.Enabled
            ? Dim
            : active
                ? MenuHighlightText
                : Accent;

        using var pen = new Pen(checkColor, Math.Max(1.8f, 2.2f * s))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLines(pen,
        [
            new PointF(x, y + checkH * 0.48f),
            new PointF(x + checkW * 0.38f, y + checkH),
            new PointF(x + checkW, y + 1f * s),
        ]);
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        float s = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        float margin = Math.Max(13f, 15f * s);
        int y = e.Item.Height / 2;
        e.Graphics.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(Pal.Separator, Math.Max(1f, 1f * s));
        e.Graphics.DrawLine(pen, margin, y, e.Item.Width - margin, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Item is not ToolStripMenuItem { HasDropDown: true })
        {
            return;
        }

        float s = (e.Item.Owner?.DeviceDpi ?? 96) / 96f;
        float w = 4.5f * s;
        float h = 8.5f * s;
        float cx = e.Item.Width - 16f * s - w / 2f;
        float cy = e.Item.Size.Height / 2f;
        bool active = IsRowActive(e.Item);
        Color arrowColor = active ? MenuHighlightText : Dim;
        using var pen = new Pen(arrowColor, Math.Max(1.4f, 1.8f * s))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen,
        [
            new PointF(cx - w / 2f, cy - h / 2f),
            new PointF(cx + w / 2f, cy),
            new PointF(cx - w / 2f, cy + h / 2f),
        ]);
    }
}
