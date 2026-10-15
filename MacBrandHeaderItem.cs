using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Numlock;

/// <summary>
/// macOS 26 风格系统托盘右键菜单“顶部专属品牌状态卡片”：
/// · 位于菜单最顶部的非激活态原生状态行（不可选中、无悬停高亮胶囊、点击无动作）
/// · 左侧：18px 连续曲率方圆角（Squircle）应用图标 + 加粗排版品牌名“Numlock”（微软雅黑 13px Bold 700）
/// · 右侧：精细磨砂半透明的版本徽标胶囊（“v1.5.3”）
/// · 横跨菜单全宽，气派舒展，确保各 DPI 下极致美观
/// </summary>
internal sealed class MacBrandHeaderItem : ToolStripMenuItem
{
    private readonly string _version;
    private readonly Func<bool> _isDark;

    public override bool CanSelect => false;

    public MacBrandHeaderItem(string version, Func<bool> isDark)
    {
        _version = version;
        _isDark = isDark;
        // 使用全角空白符为菜单布局引擎提供标准宽度基准（约 185px @96dpi），确保品牌名与徽标胶囊舒展对齐
        Text = "Numlock\u3000\u3000\u3000\u3000\u3000";
    }

    public override Size GetPreferredSize(Size constrainingSize)
    {
        float scale = (Owner?.DeviceDpi ?? 96) / 96f;
        int h = (int)Math.Round(32 * scale);
        int w = (int)Math.Round(185 * scale);
        return new Size(w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // 不执行基类默认高亮与文字绘制，完全按 macOS 26 专属品牌状态卡片自绘
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        float scale = (Owner?.DeviceDpi ?? 96) / 96f;
        bool dark = _isDark();
        int w = Width;
        int h = Height;

        Color fg = dark ? Color.FromArgb(255, 245, 245, 247) : Color.FromArgb(255, 29, 29, 31);

        // 1. 左侧：18px 连续曲率方圆角（Squircle）应用图标
        int iconMarginX = (int)Math.Round(12 * scale);
        int iconSize = (int)Math.Round(18 * scale);
        float iconY = (h - iconSize) / 2f;

        using (var appTile = IconFactory.RenderAppTile(iconSize))
        {
            g.DrawImage(appTile, iconMarginX, (int)Math.Round(iconY), iconSize, iconSize);
        }

        // 2. 品牌名：加粗排版（Bold 700 微软雅黑 13px）
        int textX = iconMarginX + iconSize + (int)Math.Round(8 * scale);
        using (var brandFont = new Font("Microsoft YaHei", 13f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            var brandSize = TextRenderer.MeasureText(g, "Numlock", brandFont);
            int textY = (h - brandSize.Height) / 2;
            TextRenderer.DrawText(g, "Numlock", brandFont,
                new Rectangle(textX, textY, brandSize.Width, brandSize.Height),
                fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        // 3. 右侧：精细磨砂半透明版本徽标胶囊
        string verText = "v" + _version;
        using (var verFont = new Font("Microsoft YaHei", 10.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            var verSize = TextRenderer.MeasureText(g, verText, verFont);
            int badgeW = verSize.Width + (int)Math.Round(12 * scale);
            int badgeH = (int)Math.Round(18 * scale);
            int badgeX = w - (int)Math.Round(12 * scale) - badgeW;
            int badgeY = (h - badgeH) / 2;

            var badgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);
            using var badgePath = Gfx.RoundedRect(badgeRect, badgeH / 2f);

            Color badgeFill = dark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(14, 0, 0, 0);
            Color badgeStroke = dark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
            Color badgeFg = dark ? Color.FromArgb(190, 245, 245, 247) : Color.FromArgb(140, 29, 29, 31);

            using (var fillBrush = new SolidBrush(badgeFill))
            {
                g.FillPath(fillBrush, badgePath);
            }
            using (var strokePen = new Pen(badgeStroke, 1f))
            {
                g.DrawPath(strokePen, badgePath);
            }

            TextRenderer.DrawText(g, verText, verFont,
                new Rectangle(badgeX, badgeY, badgeW, badgeH),
                badgeFg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}
