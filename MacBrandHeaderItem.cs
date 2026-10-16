using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Numlock;

/// <summary>
/// 托盘右键菜单顶部的专属品牌状态卡片（macOS Menu Bar Extra 风格）：
/// 位于菜单最顶部的原生状态行；
/// 第一行：左侧 18px 连续曲率方圆角（Squircle）应用图标 + 加粗排版品牌名“Numlock”，右侧精细磨砂半透明版本徽标胶囊；
/// 第二行：粗体 700 实时状态 + 绿/橙呼吸圆点。
/// </summary>
internal sealed class MacBrandHeaderItem : ToolStripMenuItem
{
    private bool _enforce;
    private bool _numOn;
    private readonly Func<bool>? _isDark;

    public MacBrandHeaderItem(Func<bool>? isDark = null)
    {
        AutoSize = false;
        Size = new Size(216, 56);
        ToolTipText = "点击查看关于软件…";
        _isDark = isDark;
    }

    public MacBrandHeaderItem(string? version, Func<bool>? isDark = null) : this(isDark) { }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (Owner is not null)
            Owner.Cursor = Cursors.Hand;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (Owner is not null)
            Owner.Cursor = Cursors.Default;
    }

    public void UpdateStatus(bool enforce, bool numOn)
    {
        if (_enforce != enforce || _numOn != numOn)
        {
            _enforce = enforce;
            _numOn = numOn;
            Invalidate();
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        float s = (Owner?.DeviceDpi ?? 96) / 96f;
        int w = (int)Math.Round(216 * s);
        int h = (int)Math.Round(56 * s);
        return new Size(w, h);
    }

    public void RenderCard(Graphics g, ToolStrip? toolStrip)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        float s = (toolStrip?.DeviceDpi ?? Owner?.DeviceDpi ?? 96) / 96f;
        float padX = Math.Max(5f, 6f * s);
        float padY = Math.Max(2f, 3f * s);
        float w = Width - padX * 2f;
        float h = Height - padY * 2f;
        var cardRect = new RectangleF(padX, padY, w, h);
        float corner = Math.Max(6f, 7f * s);

        bool isDark = _isDark != null ? _isDark() : !SystemTheme.UsesLightTaskbar();
        bool hover = Selected;

        // 顶栏专属卡片底色（macOS 顶栏状态行：沉静温润衬底 + 极细边框）
        Color cardBg = isDark
            ? (hover ? Color.FromArgb(0x38, 0x38, 0x3E) : Color.FromArgb(0x2E, 0x2E, 0x33))
            : (hover ? Color.FromArgb(0xEA, 0xEA, 0xF0) : Color.FromArgb(0xF2, 0xF2, 0xF6));

        Color cardBorder = isDark
            ? Color.FromArgb(0x44, 0x44, 0x4A)
            : Color.FromArgb(0xDC, 0xDC, 0xE2);

        using (var path = Gfx.RoundedRect(cardRect, corner))
        {
            using var brush = new SolidBrush(cardBg);
            g.FillPath(brush, path);
            using var pen = new Pen(cardBorder, Math.Max(1f, 1f * s));
            g.DrawPath(pen, path);
        }

        // 第一行：[18px 连续曲率方圆角图标] + Numlock（加粗 Bold 700 品牌名，极简纯粹不标版本号）
        float iconSize = Math.Max(16f, 18f * s);
        float iconX = cardRect.X + 10f * s;
        float line1Y = cardRect.Y + 8f * s;
        var iconRect = new RectangleF(iconX, line1Y + 1f * s, iconSize, iconSize);

        using (var appTile = IconFactory.RenderAppTile((int)Math.Round(iconSize)))
        {
            g.DrawImage(appTile, iconRect);
        }

        // Numlock 品牌名（Bold 700 微软雅黑，基准字阶）
        float titleX = iconRect.Right + 8f * s;
        using (var brandFont = new Font("Microsoft YaHei", 13f * s, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            Color brandFg = isDark ? Color.FromArgb(0xF2, 0xF2, 0xF7) : Color.FromArgb(0x1D, 0x1D, 0x1F);
            TextRenderer.DrawText(g, "Numlock", brandFont,
                new Point((int)Math.Round(titleX), (int)Math.Round(line1Y)),
                brandFg, TextFormatFlags.NoPadding);
        }

        // 第二行：状态呼吸圆点 + 实时状态（粗体 700 + 绿/橙呼吸圆点）
        float line2Y = cardRect.Y + 30f * s;
        Color dotColor = (!_numOn && !_enforce)
            ? Color.FromArgb(0xFE, 0xBC, 0x2E)    // TrafficYellow (橙黄)
            : Color.FromArgb(0x28, 0xC8, 0x40);   // TrafficGreen (Apple绿)
        float dotDiameter = Math.Max(6f, 6.5f * s);
        float dotX = iconX + 1.5f * s;
        float dotY = line2Y + 4f * s;

        using (var dotBrush = new SolidBrush(dotColor))
            g.FillEllipse(dotBrush, dotX, dotY, dotDiameter, dotDiameter);

        string statusText = _enforce
            ? "运行中 · 保持常开已就绪"
            : (_numOn ? "运行中 · 数字小键盘已开启" : "运行中 · 数字小键盘已关闭");

        float statusX = dotX + dotDiameter + 6f * s;
        var statusRect = new Rectangle(
            (int)Math.Round(statusX),
            (int)Math.Round(line2Y),
            Math.Max(0, (int)Math.Round(cardRect.Right - statusX - 8f * s)),
            (int)Math.Round(18f * s));

        using (var statusFont = new Font("Microsoft YaHei", 12f * s, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            Color statusFg = isDark ? Color.FromArgb(0xF2, 0xF2, 0xF7) : Color.FromArgb(0x1D, 0x1D, 0x1F);
            TextRenderer.DrawText(g, statusText, statusFont,
                statusRect,
                statusFg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        RenderCard(e.Graphics, Owner);
    }
}
