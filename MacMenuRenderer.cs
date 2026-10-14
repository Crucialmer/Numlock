using System.Drawing.Drawing2D;

namespace Numlock;

/// <summary>
/// macOS 风格菜单几何指标：全部按“逻辑像素 × DPI 缩放”计算，
/// 保证 100%–400% 缩放下观感一致（高 DPI / 4K / 8K 适配）。
/// </summary>
internal readonly struct MenuMetrics
{
    public readonly float Scale;            // DeviceDpi / 96
    public readonly int MenuPad;            // 菜单内容与面板边缘的纵向留白（逻辑 6；横向留白由系统图标栏决定）
    public readonly int ItemPadV;           // 菜单项上下留白（逻辑 7，行高 ≈ 26 @96dpi）
    public readonly int CheckSize;          // 勾选符号尺寸（逻辑 11；macOS 菜单勾子略小于装订线，两侧各留 ≈4 逻辑像素）
    public readonly float HighlightRadius;  // 高亮圆角（逻辑 6）
    public readonly float CornerRadius;     // 菜单面板圆角（DWM 圆角 = 8 / 自绘区域 = 10）

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
/// macOS 风格菜单渲染器：圆角面板 + 圆角蓝色高亮 + 自绘勾选/分隔线/子菜单箭头。
/// 配色（Dark）由 TrayApp 随系统主题更新；几何量按菜单窗口 DPI 实时计算。
/// </summary>
internal sealed class MacMenuRenderer : ToolStripRenderer
{
    /// <summary>深色配色（由 TrayApp 随系统主题更新）。</summary>
    public bool Dark { get; set; }

    /// <summary>每个菜单窗口是否已获得 DWM 圆角（主菜单与子菜单各记一份：两者结果可能不同，
    /// 共享单一标志会让角部半径/裁剪互相污染）。未记录时按 true（与 DWM 圆角一致的自绘半径）。</summary>
    private readonly Dictionary<ToolStrip, bool> _dwmCorners = new();

    /// <summary>记录某窗口的 DWM 圆角结果。</summary>
    public void SetDwmCorners(ToolStrip strip, bool rounded) => _dwmCorners[strip] = rounded;

    /// <summary>查询某窗口是否已由 DWM 圆角（null 或未记录 = true）。</summary>
    public bool CornersRounded(ToolStrip? strip) => strip == null || !_dwmCorners.TryGetValue(strip, out bool rounded) || rounded;

    private Color Bg => Dark ? Color.FromArgb(255, 44, 44, 46) : Color.FromArgb(255, 245, 245, 247);
    private Color Fg => Dark ? Color.FromArgb(255, 242, 242, 247) : Color.FromArgb(255, 29, 29, 31);
    private Color Accent => Dark ? Color.FromArgb(255, 10, 132, 255) : Color.FromArgb(255, 0, 122, 255);
    private Color PressedAccent => Dark ? Color.FromArgb(255, 8, 110, 214) : Color.FromArgb(255, 0, 100, 214);
    private Color Stroke => Dark ? Color.FromArgb(42, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0);
    private Color SeparatorColor => Dark ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(38, 0, 0, 0);

    // 禁用项文字色：TextRenderer（GDI）不支持 alpha，需与底色手工混合成实色
    private Color DisabledFg => Blend(Fg, Bg, 0.45f);

    private static Color Blend(Color fg, Color bg, float fgWeight) => Color.FromArgb(
        (int)Math.Round(fg.R * fgWeight + bg.R * (1 - fgWeight)),
        (int)Math.Round(fg.G * fgWeight + bg.G * (1 - fgWeight)),
        (int)Math.Round(fg.B * fgWeight + bg.B * (1 - fgWeight)));

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        // 整矩形铺底色；窗口圆角由 DWM 裁剪或 Region 兜底完成
        using var brush = new SolidBrush(Bg);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.ToolStrip.Size));
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var m = MenuMetrics.For(e.ToolStrip.DeviceDpi, CornersRounded(e.ToolStrip));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Stroke, 1f);
        if (e.ToolStrip is ToolStripDropDown { OwnerItem: not null })
        {
            // 子菜单：左缘与父面板右缘相邻（窗口左缘 = 父面板右缘），左侧边线不画——否则接缝处两条 1px 描边
            // 并排、线宽翻倍；左上/左下角也不做圆角（窗口 Region 按“仅右侧圆角”裁剪），角部不再露背景缺口。
            float x = 0.5f, y = 0.5f;
            float w = e.ToolStrip.Width - 1f, h = e.ToolStrip.Height - 1f;
            float r = Math.Max(2f, m.CornerRadius - 0.5f);
            e.Graphics.DrawLine(pen, x, y, x + w - r, y);                              // 上边
            e.Graphics.DrawArc(pen, x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);          // 右上角
            e.Graphics.DrawLine(pen, x + w, y + r, x + w, y + h - r);                  // 右边
            e.Graphics.DrawArc(pen, x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90); // 右下角
            e.Graphics.DrawLine(pen, x + w - r, y + h, x, y + h);                      // 下边
            return;
        }
        var outer = new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1f, e.ToolStrip.Height - 1f);
        using var path = Gfx.RoundedRect(outer, Math.Max(2f, m.CornerRadius - 0.5f));
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!IsRowActive(e.Item))
        {
            return;
        }
        DrawHighlightRow(e.Graphics, e.ToolStrip, e.Item, e.Item.Pressed);
    }

    /// <summary>行是否按“激活”绘制：悬停/按下，或该项的子菜单正打开（框架会把父项标成不可用样式，这里按 macOS 习惯保持高亮）。</summary>
    private static bool IsRowActive(ToolStripItem item) =>
        item.Selected || item.Pressed || IsSubmenuOpen(item);

    private static bool IsSubmenuOpen(ToolStripItem item) =>
        item is ToolStripMenuItem { HasDropDown: true } mi && mi.DropDown.Visible;

    /// <summary>圆角高亮块（悬停/子菜单打开共用）。
    /// 注意：菜单项绘制在“项本地坐标”系（0,0=项左上角），不能用菜单坐标的 e.Item.Bounds。</summary>
    private void DrawHighlightRow(Graphics g, ToolStrip? strip, ToolStripItem item, bool pressed)
    {
        var m = MenuMetrics.For(strip?.DeviceDpi ?? 96, CornersRounded(strip));
        var b = new Rectangle(Point.Empty, item.Size); // 项本地坐标
        var rect = new RectangleF(
            b.X + 3f * m.Scale,
            b.Y + 1f * m.Scale,
            b.Width - 6f * m.Scale,
            b.Height - 2f * m.Scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Gfx.RoundedRect(rect, m.HighlightRadius);
        using var brush = new SolidBrush(pressed ? PressedAccent : Accent);
        g.FillPath(brush, path);
    }

    /// <summary>各菜单实测的“项文字起点”x（项本地坐标，由 OnRenderItemText 记录）：
    /// 勾选右缘与分隔线左端都以它为基准，才能在任意 DPI 下与文字对齐（框架的装订线宽度并非按逻辑像素线性缩放）。</summary>
    private readonly Dictionary<ToolStrip, int> _textLeft = new();

    private int? TextLeftOf(ToolStrip? strip) =>
        strip != null && _textLeft.TryGetValue(strip, out int x) ? x : null;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        bool submenuOpen = IsSubmenuOpen(e.Item);
        bool active = IsRowActive(e.Item);
        if (e.ToolStrip != null)
        {
            _textLeft[e.ToolStrip] = e.TextRectangle.X; // 供勾选/分隔线对齐用（每次绘制都刷新，DPI 变化自动跟上）
        }

        if (submenuOpen && !e.Item.Selected && !e.Item.Pressed)
        {
            // 子菜单打开时框架不派发背景回调（父项被视为不可用），这里补画高亮块
            DrawHighlightRow(e.Graphics, e.ToolStrip, e.Item, pressed: false);
        }

        bool enabled = e.Item.Enabled || submenuOpen;
        Color color = active ? Color.White : (enabled ? Fg : DisabledFg);
        // 文字墨迹垂直居中：框架给的 TextRectangle + VerticalCenter 居中的是“字符行盒”，
        // 而中文行盒上下明显不对称（实测墨迹中心比行中心高 8px@96dpi，高亮行/普通行一致），
        // 勾选与箭头却按行居中 → 看起来“文字浮在上半行、勾子下坠”。这里把文字行盒按实测量到的
        // 偏移下移，让墨迹中心落在行中心；水平位置沿用框架的 TextRectangle，墨迹仍在项矩形内（不会裁切）。
        int textH = Math.Max(1, e.TextRectangle.Height);
        int inkShift = InkCenterOffset(e.TextFont, textH, e.Text);
        Rectangle text = new(
            e.TextRectangle.X,
            e.Item.Size.Height / 2 - textH / 2 + inkShift,
            e.TextRectangle.Width,
            textH);
        TextRenderer.DrawText(
            e.Graphics, e.Text, e.TextFont, text, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    private static readonly Dictionary<string, int> InkOffsetCache = new();

    /// <summary>
    /// “文字所在矩形中心 → 文字墨迹中心”的垂直偏移（像素；正值 = 墨迹偏上，需要把矩形下移同样距离）。
    /// 用一次性探针位图实测：与真实绘制同字体、同 TextFormatFlags、同矩形高度、同文本
    /// （必须用实际文本——墨迹范围取决于字形，含不带下伸部的西文占位串会量偏）；
    /// 按“字体 + 矩形高 + 文本”缓存；实测失败则返回 0（退回框架默认观感），不影响菜单可用性。
    /// </summary>
    private static int InkCenterOffset(Font? font, int rectHeight, string? text)
    {
        if (font == null || rectHeight <= 0 || string.IsNullOrEmpty(text))
        {
            return 0;
        }
        string key = $"{font.FontFamily.Name}|{font.SizeInPoints:F2}|{font.Style}|{font.Unit}|{rectHeight}|{text}";
        if (InkOffsetCache.TryGetValue(key, out int cached))
        {
            return cached;
        }
        int shift = 0;
        try
        {
            int top = Math.Max(4, font.Height);
            using var probe = new Bitmap(Math.Max(64, font.Height * 4 * (text.Length + 1)), rectHeight + top * 2);
            using (var g = Graphics.FromImage(probe))
            {
                g.Clear(Color.Black);
                TextRenderer.DrawText(
                    g, text, font, new Rectangle(0, top, probe.Width, rectHeight), Color.White,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            int first = -1;
            int last = -1;
            for (int y = top; y < top + rectHeight; y++)
            {
                bool ink = false;
                for (int x = 0; x < probe.Width; x++)
                {
                    if (probe.GetPixel(x, y).R > 110) // 黑底白字：>110 即墨迹（含抗锯齿）
                    {
                        ink = true;
                        break;
                    }
                }
                if (ink)
                {
                    if (first < 0)
                    {
                        first = y;
                    }
                    last = y;
                }
            }
            if (first >= 0)
            {
                int rectCenter = top + rectHeight / 2;
                shift = rectCenter - (first + last) / 2;
            }
        }
        catch
        {
            shift = 0;
        }
        InkOffsetCache[key] = shift;
        return shift;
    }

    /// <summary>自检用：给定字体、文字矩形高度与文本下测得的墨迹居中偏移。</summary>
    internal static int InkCenterOffsetForTest(Font font, int rectHeight, string text) => InkCenterOffset(font, rectHeight, text);

    /// <summary>
    /// 勾选符号（macOS 风格）：框架在“已勾选 + 图标栏/勾选栏可见”时回调到这里，
    /// e.ImageRectangle 即勾选区域（左侧装订线，项本地坐标），按区域居中绘制。
    /// </summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var m = MenuMetrics.For(e.ToolStrip?.DeviceDpi ?? 96, CornersRounded(e.ToolStrip));
        bool active = IsRowActive(e.Item);
        // 勾选右缘距“文字起点”固定 4 逻辑像素（任何 DPI 下一致，接近 macOS 的留白）；
        // 文字起点来自实测缓存，未测得时退回框架的装订线区域居中。
        float centerX;
        if (TextLeftOf(e.ToolStrip) is int textLeft)
        {
            centerX = textLeft - 4f * m.Scale - m.CheckSize / 2f;
        }
        else
        {
            Rectangle area = e.ImageRectangle.Width > 0 && e.ImageRectangle.Height > 0
                ? e.ImageRectangle
                : new Rectangle(Point.Empty, e.Item.Size);
            centerX = area.X + area.Width / 2f;
        }
        // 颜色用标签字色（macOS 习惯）：强调蓝已表示“高亮行”，再表示“已勾选”会语义重叠。
        DrawCheck(e.Graphics, m, new PointF(centerX, e.Item.Size.Height / 2f), active ? Color.White : Fg);
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        // 本项目菜单项无图标
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Item is not ToolStripMenuItem { HasDropDown: true })
        {
            return;
        }
        var m = MenuMetrics.For(e.Item.Owner?.DeviceDpi ?? 96, CornersRounded(e.Item.Owner));
        float w = 4.5f * m.Scale;
        float h = 8.5f * m.Scale;
        // 箭头位置自定（不用框架 e.ArrowRectangle，它紧贴高亮块右缘、只剩 1 设备像素）：
        // 面板右缘留 8 逻辑像素呼吸空间，与 macOS 的内边距一致。
        float cx = e.Item.Width - 8f * m.Scale - w / 2f;
        float cy = e.Item.Size.Height / 2f;
        bool active = IsRowActive(e.Item);
        using var pen = new Pen(active ? Color.White : Color.FromArgb(130, Fg), Math.Max(1.3f, 1.7f * m.Scale))
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

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var m = MenuMetrics.For(e.ToolStrip?.DeviceDpi ?? 96, CornersRounded(e.ToolStrip));
        // 同样使用“项本地坐标”（0,0=项左上角）；左端与项文字起点对齐（实测缓存），
        // 此前固定 14 逻辑像素会让分隔线比文字“戳出”（96dpi 下 22px、200% 下 6 逻辑像素）。
        float y = e.Item.Height / 2f + 0.5f;
        float leftInset = TextLeftOf(e.ToolStrip) is int textLeft ? textLeft : 20f * m.Scale;
        float rightInset = 14f * m.Scale;
        using var pen = new Pen(SeparatorColor, 1f);
        e.Graphics.DrawLine(pen, leftInset, y, e.Item.Width - rightInset, y);
    }

    /// <summary>macOS 风格勾选符号（圆角折线对勾；以 center 为中心）。</summary>
    private static void DrawCheck(Graphics g, MenuMetrics m, PointF center, Color color)
    {
        float s = m.CheckSize;
        float x = center.X - s / 2f;
        float y = center.Y - s / 2f;
        using var pen = new Pen(color, Math.Max(1.4f, 1.8f * m.Scale))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawLines(pen, new[]
        {
            new PointF(x + s * 0.08f, y + s * 0.55f),
            new PointF(x + s * 0.38f, y + s * 0.86f),
            new PointF(x + s * 0.94f, y + s * 0.18f),
        });
    }
}
