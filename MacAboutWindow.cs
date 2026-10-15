using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Numlock;

/// <summary>
/// macOS 26 风格“关于软件”独立窗口：
/// · 位于屏幕右下角（任务栏上方）
/// · 连续曲率圆角矩形（Squircle）
/// · macOS 经典红点关闭控制
/// · DWM 瞬态亚克力材质 + 沉浸式深/浅双主题
/// · 键帽状态图例卡片 + 快捷操作提示 + GitHub 项目链接
/// </summary>
internal sealed class MacAboutWindow : Form
{
    private static MacAboutWindow? s_instance;

    private readonly string _version;
    private readonly bool _dark;
    private readonly float _scale;

    private readonly int _cornerRadius;
    private RectangleF _closeBtnRect;
    private RectangleF _actionBtnRect;
    private RectangleF _linkRect;

    private bool _closeHover;
    private bool _closePressed;
    private bool _actionHover;
    private bool _actionPressed;
    private bool _linkHover;

    // macOS 26 配色
    private Color BgColor => _dark ? Color.FromArgb(232, 32, 32, 36) : Color.FromArgb(238, 246, 246, 248);
    private Color FgColor => _dark ? Color.FromArgb(255, 245, 245, 247) : Color.FromArgb(255, 29, 29, 31);
    private Color SecondaryFg => _dark ? Color.FromArgb(170, 245, 245, 247) : Color.FromArgb(150, 29, 29, 31);
    private Color AccentBlue => _dark ? Color.FromArgb(255, 10, 132, 255) : Color.FromArgb(255, 0, 122, 255);
    private Color AccentBlueHover => _dark ? Color.FromArgb(255, 30, 144, 255) : Color.FromArgb(255, 20, 132, 255);
    private Color AccentBluePressed => _dark ? Color.FromArgb(255, 8, 108, 212) : Color.FromArgb(255, 0, 106, 224);
    private Color StrokeColor => _dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0);
    private Color CardBg => _dark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(14, 0, 0, 0);

    private MacAboutWindow(string version)
    {
        _version = version;
        _dark = !SystemTheme.UsesLightTaskbar();
        _scale = DeviceDpi / 96f;
        _cornerRadius = (int)(16 * _scale);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(BgColor.R, BgColor.G, BgColor.B);
        KeyPreview = true;

        int width = (int)(330 * _scale);
        int height = (int)(410 * _scale);
        ClientSize = new Size(width, height);

        // 定位在屏幕右下角（任务栏工作区边缘）
        var screen = Screen.FromPoint(Cursor.Position);
        var wa = screen.WorkingArea;
        int margin = (int)(16 * _scale);
        Location = new Point(wa.Right - width - margin, wa.Bottom - height - margin);

        // DWM 圆角 + 亚克力 + 深色帧
        HandleCreated += (_, _) =>
        {
            try
            {
                bool rounded = Native.TryRoundCorners(Handle);
                Native.SetImmersiveDarkMode(Handle, _dark);
                Native.TrySetTransientBackdrop(Handle);
                if (!rounded)
                {
                    using var path = Gfx.RoundedRect(new RectangleF(0, 0, Width, Height), _cornerRadius);
                    Region = new Region(path);
                }
            }
            catch { }
        };

        // 失去焦点时自动淡雅关闭（macOS 悬浮面板惯例）
        Deactivate += (_, _) => Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter)
        {
            Close();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        int w = ClientSize.Width;
        int h = ClientSize.Height;

        // ——— 1. 面板底色 ———
        using (var bgBrush = new SolidBrush(BgColor))
        {
            g.FillRectangle(bgBrush, ClientRectangle);
        }

        // ——— 2. macOS 顶部标题栏控制（左上角红点） ———
        float dotSize = 12 * _scale;
        float dotX = 14 * _scale;
        float dotY = 14 * _scale;
        _closeBtnRect = new RectangleF(dotX - 2, dotY - 2, dotSize + 4, dotSize + 4);

        Color dotColor = _closePressed
            ? Color.FromArgb(255, 200, 40, 35)
            : (_closeHover ? Color.FromArgb(255, 255, 90, 82) : Color.FromArgb(255, 255, 95, 86));

        using (var dotBrush = new SolidBrush(dotColor))
        {
            g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
        }

        // 悬停时红点内绘制微小 'x'
        if (_closeHover)
        {
            using var xPen = new Pen(Color.FromArgb(180, 80, 0, 0), 1.2f * _scale);
            float cx = dotX + dotSize / 2f;
            float cy = dotY + dotSize / 2f;
            float off = dotSize * 0.22f;
            g.DrawLine(xPen, cx - off, cy - off, cx + off, cy + off);
            g.DrawLine(xPen, cx + off, cy - off, cx - off, cy + off);
        }

        // 顶部居中标题
        using (var topFont = new Font("Segoe UI", 11 * _scale, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            TextRenderer.DrawText(g, "关于软件", topFont,
                new Rectangle(0, (int)(12 * _scale), w, (int)(18 * _scale)),
                SecondaryFg, TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
        }

        // ——— 3. 应用图标与品牌（居中） ———
        int iconSize = (int)(52 * _scale);
        int iconX = (w - iconSize) / 2;
        int iconY = (int)(42 * _scale);
        using (var tileBmp = IconFactory.RenderAppTile(iconSize))
        {
            g.DrawImage(tileBmp, iconX, iconY, iconSize, iconSize);
        }

        // 软件名
        using var nameFont = new Font("Segoe UI", 16 * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        var nameSize = TextRenderer.MeasureText(g, "Numlock", nameFont);
        int nameY = iconY + iconSize + (int)(8 * _scale);
        TextRenderer.DrawText(g, "Numlock", nameFont,
            new Rectangle(0, nameY, w, nameSize.Height),
            FgColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);

        // 版本胶囊徽章（Pill Badge）
        string verText = "v" + _version;
        using var verFont = new Font("Segoe UI", 10.5f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        var verSize = TextRenderer.MeasureText(g, verText, verFont);
        int badgeW = verSize.Width + (int)(14 * _scale);
        int badgeH = verSize.Height + (int)(4 * _scale);
        int badgeX = (w - badgeW) / 2;
        int badgeY = nameY + nameSize.Height + (int)(4 * _scale);

        var badgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);
        using (var badgePath = Gfx.RoundedRect(badgeRect, badgeH / 2f))
        {
            using var badgeFill = new SolidBrush(Color.FromArgb(_dark ? 40 : 25, AccentBlue));
            g.FillPath(badgeFill, badgePath);
            using var badgePen = new Pen(Color.FromArgb(_dark ? 80 : 60, AccentBlue), 1f);
            g.DrawPath(badgePen, badgePath);
        }
        TextRenderer.DrawText(g, verText, verFont,
            new Rectangle(badgeX, badgeY, badgeW, badgeH),
            AccentBlue, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        // 副标题描述
        using var subFont = new Font("Segoe UI", 11 * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        int subY = badgeY + badgeH + (int)(5 * _scale);
        TextRenderer.DrawText(g, "数字小键盘状态看护 · 保持常开", subFont,
            new Rectangle(0, subY, w, (int)(18 * _scale)),
            SecondaryFg, TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);

        // ——— 4. 状态图例与功能卡片（macOS Inset Card） ———
        int cardMargin = (int)(16 * _scale);
        int cardX = cardMargin;
        int cardY = subY + (int)(22 * _scale);
        int cardW = w - cardMargin * 2;
        int cardH = (int)(130 * _scale);

        var cardRect = new RectangleF(cardX, cardY, cardW, cardH);
        using (var cardPath = Gfx.RoundedRect(cardRect, 10 * _scale))
        {
            using var cardBrush = new SolidBrush(CardBg);
            g.FillPath(cardBrush, cardPath);
            using var cardStroke = new Pen(StrokeColor, 1f);
            g.DrawPath(cardStroke, cardPath);
        }

        // 卡片内容：4 行图例
        using var itemFont = new Font("Segoe UI", 11 * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        int rowPadX = cardX + (int)(14 * _scale);
        int startRowY = cardY + (int)(10 * _scale);
        int rowH = (int)(27 * _scale);

        DrawLegendRow(g, rowPadX, startRowY + rowH * 0, "实心键帽", "已开启（NumLock ON）", isSolid: true, hasBadge: false, itemFont);
        DrawLegendRow(g, rowPadX, startRowY + rowH * 1, "描边键帽", "已关闭（NumLock OFF）", isSolid: false, hasBadge: false, itemFont);
        DrawLegendRow(g, rowPadX, startRowY + rowH * 2, "绿色圆点", "保持常开模式已激活", isSolid: true, hasBadge: true, itemFont);
        DrawActionRow(g, rowPadX, startRowY + rowH * 3, "快捷交互", "双击切换 · 右键呼出菜单", itemFont);

        // ——— 5. 底部主页链接与操作按钮 ———
        int linkY = cardY + cardH + (int)(10 * _scale);
        string linkText = "GitHub: Crucialmer/Numlock";
        using var linkFont = new Font("Segoe UI", 11 * _scale, _linkHover ? FontStyle.Underline : FontStyle.Regular, GraphicsUnit.Pixel);
        var linkSize = TextRenderer.MeasureText(g, linkText, linkFont);
        int linkX = (w - linkSize.Width) / 2;
        _linkRect = new RectangleF(linkX, linkY, linkSize.Width, linkSize.Height);

        TextRenderer.DrawText(g, linkText, linkFont,
            new Rectangle(linkX, linkY, linkSize.Width, linkSize.Height),
            _linkHover ? AccentBlue : SecondaryFg,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        // 主按钮：“好”
        int btnW = (int)(90 * _scale);
        int btnH = (int)(28 * _scale);
        int btnX = (w - btnW) / 2;
        int btnY = linkY + linkSize.Height + (int)(10 * _scale);
        _actionBtnRect = new RectangleF(btnX, btnY, btnW, btnH);

        Color btnBg = _actionPressed
            ? AccentBluePressed
            : (_actionHover ? AccentBlueHover : AccentBlue);

        using (var btnPath = Gfx.RoundedRect(_actionBtnRect, 7 * _scale))
        {
            using var btnBrush = new SolidBrush(btnBg);
            g.FillPath(btnBrush, btnPath);
        }
        using var btnFont = new Font("Segoe UI", 11.5f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, "好", btnFont,
            new Rectangle(btnX, btnY, btnW, btnH),
            Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        // ——— 6. 面板外边框描边 ———
        using var strokePen = new Pen(StrokeColor, 1f);
        using var borderPath = Gfx.RoundedRect(new RectangleF(0.5f, 0.5f, w - 1f, h - 1f), _cornerRadius - 0.5f);
        g.DrawPath(strokePen, borderPath);
    }

    private void DrawLegendRow(Graphics g, int x, int y, string label, string desc, bool isSolid, bool hasBadge, Font font)
    {
        // 绘制微缩键帽图标（14x14）
        int sz = (int)(14 * _scale);
        int iconY = y + (int)(3 * _scale);
        using var iconBmp = IconFactory.RenderTrayIcon(sz, isSolid, _dark, hasBadge);
        g.DrawImage(iconBmp, x, iconY, sz, sz);

        int textX = x + sz + (int)(10 * _scale);
        using var boldFont = new Font(font.FontFamily, font.Size, FontStyle.Bold, GraphicsUnit.Pixel);
        var labelSize = TextRenderer.MeasureText(g, label, boldFont);
        TextRenderer.DrawText(g, label, boldFont,
            new Rectangle(textX, y, labelSize.Width, (int)(20 * _scale)),
            FgColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        int descX = textX + labelSize.Width + (int)(6 * _scale);
        TextRenderer.DrawText(g, "·  " + desc, font,
            new Rectangle(descX, y, (int)(180 * _scale), (int)(20 * _scale)),
            SecondaryFg, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private void DrawActionRow(Graphics g, int x, int y, string label, string desc, Font font)
    {
        int sz = (int)(14 * _scale);
        // 小圆点符号
        using var dotBrush = new SolidBrush(Color.FromArgb(_dark ? 120 : 160, AccentBlue));
        float dotR = 3.5f * _scale;
        g.FillEllipse(dotBrush, x + sz / 2f - dotR, y + sz / 2f - dotR + (int)(3 * _scale), dotR * 2, dotR * 2);

        int textX = x + sz + (int)(10 * _scale);
        using var boldFont = new Font(font.FontFamily, font.Size, FontStyle.Bold, GraphicsUnit.Pixel);
        var labelSize = TextRenderer.MeasureText(g, label, boldFont);
        TextRenderer.DrawText(g, label, boldFont,
            new Rectangle(textX, y, labelSize.Width, (int)(20 * _scale)),
            FgColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        int descX = textX + labelSize.Width + (int)(6 * _scale);
        TextRenderer.DrawText(g, "·  " + desc, font,
            new Rectangle(descX, y, (int)(180 * _scale), (int)(20 * _scale)),
            SecondaryFg, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    // ——— 鼠标交互 ———

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (_closeBtnRect.Contains(e.Location))
            {
                _closePressed = true;
                Invalidate();
            }
            else if (_actionBtnRect.Contains(e.Location))
            {
                _actionPressed = true;
                Invalidate();
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_closePressed && _closeBtnRect.Contains(e.Location))
        {
            Close();
            return;
        }
        if (_actionPressed && _actionBtnRect.Contains(e.Location))
        {
            Close();
            return;
        }
        if (_linkRect.Contains(e.Location))
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/Crucialmer/Numlock") { UseShellExecute = true });
            }
            catch { }
            return;
        }

        _closePressed = false;
        _actionPressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool ch = _closeBtnRect.Contains(e.Location);
        bool ah = _actionBtnRect.Contains(e.Location);
        bool lh = _linkRect.Contains(e.Location);

        if (ch != _closeHover || ah != _actionHover || lh != _linkHover)
        {
            _closeHover = ch;
            _actionHover = ah;
            _linkHover = lh;
            Cursor = (ch || ah || lh) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_closeHover || _actionHover || _linkHover)
        {
            _closeHover = false;
            _actionHover = false;
            _linkHover = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        s_instance = null;
        base.OnFormClosed(e);
    }

    /// <summary>单例打开 macOS 风格“关于软件”右下角窗口。</summary>
    public static void ShowAbout(string version)
    {
        if (s_instance != null && !s_instance.IsDisposed)
        {
            s_instance.BringToFront();
            s_instance.Activate();
            return;
        }

        s_instance = new MacAboutWindow(version);
        s_instance.Show();
    }
}
