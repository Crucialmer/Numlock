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
        MenuPad = Math.Max(3, (int)Math.Round(6 * scale));
        ItemPadV = (int)Math.Round(7 * scale);
        CheckSize = (int)Math.Round(11 * scale);
        HighlightRadius = Math.Max(2f, 6 * scale);
        CornerRadius = (dwmCorners ? 8f : 10f) * scale;
    }

    public static MenuMetrics For(int dpi, bool dwmCorners = true) => new(dpi / 96f, dwmCorners);
}

/// <summary>
/// macOS 原生状态栏应用（Menu Bar Extra）风格菜单渲染器：
/// 1. 清空左侧灰色边栏槽（ImageMargin），整个菜单底色一体纯净；
/// 2. 统一全量菜单文本起始列至 X = 38px，对号与文字拉开 12px+ 舒适间距；
/// 3. 矢量圆角 Apple 蓝勾，胶囊内侧 X = 14px 居中，选中态纯白反白；
/// 4. 完美承接顶部 MacBrandHeaderItem 专属品牌状态卡片；
/// 5. 浅色/深色主题动态适配（浅色 Apple 蓝高亮，深色微光白半透明胶囊）。
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

    public Color MenuBack => Dark ? Color.FromArgb(0x2A, 0x2A, 0x2C) : Color.White;
    public Color MenuBorder => Dark ? Color.FromArgb(0x3C, 0x3C, 0x3E) : Color.FromArgb(0xE0, 0xE0, 0xE5);
    public Color MenuText => Dark ? Color.FromArgb(0xF2, 0xF2, 0xF7) : Color.FromArgb(0x1D, 0x1D, 0x1F);
    public Color MenuHighlightBack => Dark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(0x00, 0x7A, 0xFF);
    public Color MenuPressedBack => Dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(0x00, 0x62, 0xCC);
    public Color MenuHighlightText => Color.White;
    public Color Dim => Dark ? Color.FromArgb(0x98, 0x98, 0x9D) : Color.FromArgb(0x8E, 0x8E, 0x93);
    public Color Accent => Dark ? Color.FromArgb(0x0A, 0x84, 0xFF) : Color.FromArgb(0x00, 0x7A, 0xFF);

    /// <summary>清空系统默认左侧灰色图像边栏，保证整个菜单底色一体纯净</summary>
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // 不绘制灰色槽条，保持通透
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(MenuBack);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.ToolStrip.Size));
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(MenuBorder, 1f);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is MacBrandHeaderItem header)
        {
            header.RenderCard(e.Graphics, e.ToolStrip);
            return;
        }

        if (IsRowActive(e.Item) && e.Item.Enabled)
        {
            float s = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
            float padX = Math.Max(5f, 6f * s);
            float padY = Math.Max(2f, 2f * s);
            var rect = new RectangleF(padX, padY, e.Item.Width - padX * 2f, e.Item.Height - padY * 2f);
            using var path = Gfx.RoundedRect(rect, Math.Max(5f, 6f * s));
            Color bg = e.Item.Pressed ? MenuPressedBack : MenuHighlightBack;
            using var brush = new SolidBrush(bg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }
    }

    private static bool IsRowActive(ToolStripItem item) =>
        item.Selected || item.Pressed || (item is ToolStripMenuItem mi && mi.DropDown.Visible);

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is MacBrandHeaderItem)
            return;

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

    /// <summary>勾选标记改为矢量对勾（距左 15px，离外框与胶囊边缘舒展对称，选中态纯白反白，macOS 原生观感）</summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item is MacBrandHeaderItem)
            return;

        float s = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 垂直居中、距左 15px，告别紧贴边框，在悬停胶囊内舒展居中
        float checkW = Math.Max(10f, 12f * s);
        float checkH = Math.Max(8f, 10f * s);
        float x = Math.Max(13f, 15f * s);
        float y = (e.Item.Height - checkH) / 2f;

        bool active = IsRowActive(e.Item);
        Color checkColor = !e.Item.Enabled
            ? Dim
            : active
                ? MenuHighlightText   // 浅色/深色选中态胶囊下纯白反白 (White Indicator)
                : Accent;             // 未选中态为 Apple 原厂系统蓝

        using var pen = new Pen(checkColor, Math.Max(1.8f, 2.2f * s))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        var pts = new[]
        {
            new PointF(x, y + checkH * 0.48f),
            new PointF(x + checkW * 0.38f, y + checkH),
            new PointF(x + checkW, y + 1f * s),
        };
        g.DrawLines(pen, pts);
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        // 本项目菜单项无独立图片槽
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        float s = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        float margin = Math.Max(12f, 14f * s);
        int y = e.Item.Height / 2;
        e.Graphics.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(MenuBorder, Math.Max(1f, 1f * s));
        e.Graphics.DrawLine(pen, margin, y, e.Item.Width - margin, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Item is not ToolStripMenuItem { HasDropDown: true })
            return;

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
        e.Graphics.DrawLines(pen, new[]
        {
            new PointF(cx - w / 2f, cy - h / 2f),
            new PointF(cx + w / 2f, cy),
            new PointF(cx - w / 2f, cy + h / 2f),
        });
    }
}
