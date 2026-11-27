using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Numlock;

/// <summary>
/// 托盘右键菜单顶部的 macOS 26 Liquid Glass 专属品牌状态卡片（Menu Bar Extra 风格）：
/// · 磨砂光学玻璃内嵌卡片 + 顶部高光边缘反射 + 悬停抬升反馈
/// · 第一行：16px 连续曲率方圆角磁贴（缓存零分配）+ 加粗品牌名“Numlock”（与菜单项 38px 标尺垂直对齐）
/// · 第二行：发光呼吸光晕绿/橙状态指示圆点 + 粗体实时状态文案
/// </summary>
internal sealed class MacBrandHeaderItem : ToolStripMenuItem
{
    private bool _enforce;
    private bool _numOn;
    private readonly Func<bool>? _isDark;
    private Font? _cachedFont;
    private float _cachedScale;

    public MacBrandHeaderItem(Func<bool>? isDark = null)
    {
        AutoSize = false;
        Size = new Size(168, 48);
        ToolTipText = "点击查看关于软件…";
        _isDark = isDark;
    }

    public MacBrandHeaderItem(string? version, Func<bool>? isDark = null) : this(isDark) { }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (Owner is not null)
        {
            Owner.Cursor = Cursors.Hand;
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (Owner is not null)
        {
            Owner.Cursor = Cursors.Default;
        }
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
        return new Size((int)Math.Round(168 * s), (int)Math.Round(48 * s));
    }

    private Font GetBoldFont(float scale)
    {
        if (_cachedFont != null && Math.Abs(_cachedScale - scale) < 0.01f)
        {
            return _cachedFont;
        }
        _cachedFont?.Dispose();
        _cachedScale = scale;
        _cachedFont = LiquidGlass.CreateFont(12f * scale, FontStyle.Bold);
        return _cachedFont;
    }

    public void RenderCard(Graphics g, ToolStrip? toolStrip)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        float s = (toolStrip?.DeviceDpi ?? Owner?.DeviceDpi ?? 96) / 96f;
        float padX = Math.Max(5f, 6f * s);
        float padY = Math.Max(2f, 2.5f * s);
        var cardRect = new RectangleF(padX, padY, Width - padX * 2f, Height - padY * 2f);
        float corner = Math.Max(6.5f, 7.5f * s);

        bool isDark = _isDark != null ? _isDark() : !SystemTheme.UsesLightTaskbar();
        var pal = LiquidGlass.Get(isDark);
        LiquidGlass.DrawInsetCard(g, cardRect, corner, pal, hover: Selected, strokeWidth: Math.Max(1f, 1f * s));

        // 第一行：16px 连续曲率方圆角图标（复用缓存）+ Numlock 品牌名（与菜单项 38px 标尺对齐）
        float iconSize = Math.Max(15f, 16f * s);
        float iconX = cardRect.X + 7f * s;
        float line1Y = cardRect.Y + 5.5f * s;
        var iconRect = new RectangleF(iconX, line1Y + 0.5f * s, iconSize, iconSize);
        LiquidGlass.DrawCachedAppTile(g, iconRect, (int)Math.Round(iconSize * 2f));

        Font boldFont = GetBoldFont(s);
        float titleX = iconRect.Right + 7f * s;
        TextRenderer.DrawText(
            g,
            "Numlock",
            boldFont,
            new Point((int)Math.Round(titleX), (int)Math.Round(line1Y)),
            pal.TextPrimary,
            TextFormatFlags.NoPadding);

        // 第二行：macOS 26 柔和发光光晕 + 绿/橙状态指示点 + 实时状态文案
        float line2Y = cardRect.Y + 23.5f * s;
        Color dotColor = (!_numOn && !_enforce) ? pal.WarningAmber : pal.SuccessGreen;
        float dotDiameter = Math.Max(5.5f, 6f * s);
        float dotX = iconX + 1.5f * s;
        float dotY = line2Y + 4f * s;

        // 外围液态微光光晕环
        float haloPad = 2.2f * s;
        using (var haloBrush = new SolidBrush(Color.FromArgb(isDark ? 68 : 54, dotColor)))
        {
            g.FillEllipse(haloBrush, dotX - haloPad, dotY - haloPad, dotDiameter + haloPad * 2f, dotDiameter + haloPad * 2f);
        }
        using (var dotBrush = new SolidBrush(dotColor))
        {
            g.FillEllipse(dotBrush, dotX, dotY, dotDiameter, dotDiameter);
        }

        string statusText = _enforce
            ? "保持常开已激活"
            : (_numOn ? "数字小键盘已开启" : "数字小键盘已关闭");

        float statusX = dotX + dotDiameter + 6f * s;
        var statusRect = new Rectangle(
            (int)Math.Round(statusX),
            (int)Math.Round(line2Y),
            Math.Max(0, (int)Math.Round(cardRect.Right - statusX - 8f * s)),
            (int)Math.Round(16f * s));

        TextRenderer.DrawText(
            g,
            statusText,
            boldFont,
            statusRect,
            pal.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        RenderCard(e.Graphics, Owner);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cachedFont?.Dispose();
            _cachedFont = null;
        }
        base.Dispose(disposing);
    }
}
