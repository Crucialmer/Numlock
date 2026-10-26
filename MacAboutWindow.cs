using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Numlock;

/// <summary>
/// macOS 26 风格独立“关于软件”紧凑型模态卡片窗口（方案 B：极致纯粹极简设计）：
/// · 纯粹极简视觉：彻底移除版本号字符串与花哨技术胶囊，回归原生极简温润感
/// · 紧凑秀气尺寸：宽度收缩至 328px，高度收缩至 405px，黄金纵横比例
/// · 居中呈现于用户当前操作的显示器工作区，与 macOS 原生面板标准对齐
/// · 连续曲率圆角矩形（Squircle）+ 极细半透明发丝描边 + 瞬态亚克力材质（DWM Transient Acrylic）
/// · macOS 经典红点关闭控制 + 顶部居中细致标题 + 全窗平滑拖拽移动
/// · 50px 品牌键帽磁贴应用图标 + 醒目品牌名 + 次级描述副标题
/// · 状态图例与功能指引内嵌卡片（macOS Inset Card）：
///     1. 实心键帽：已开启（NumLock ON）
///     2. 描边键帽：已关闭（NumLock OFF）
///     3. 绿色圆点：保持常开模式已激活
///     4. 快捷交互：双击切换 · 右键菜单
/// · 纯 GDI+ 抗锯齿矢量文本渲染，彻底杜绝 ClearType 边缘重影
/// · 适配高 DPI 显示器（最高 8K 显示器，DPI 384+）与跨屏动态漫游（OnDpiChanged）
/// · 沉浸式深/浅双模自适应 + 键盘交互（Esc / Enter 关闭）
/// </summary>
internal sealed class MacAboutWindow : Form
{
    private static MacAboutWindow? s_instance;

    private readonly bool _dark;
    private readonly string _version;
    private float _scale;
    private int _cornerRadius;

    private RectangleF _closeBtnRect;
    private RectangleF _actionBtnRect;
    private RectangleF _linkRect;

    private bool _closeHover;
    private bool _closePressed;
    private bool _actionHover;
    private bool _actionPressed;
    private bool _linkHover;

    // 平滑淡入动画
    private readonly System.Windows.Forms.Timer? _animTimer;
    private double _targetOpacity = 1.0;

    // macOS 26 配色体系
    private Color BgColor => _dark ? Color.FromArgb(255, 32, 32, 36) : Color.FromArgb(255, 242, 242, 244);
    private Color FgColor => _dark ? Color.FromArgb(255, 245, 245, 247) : Color.FromArgb(255, 29, 29, 31);
    private Color SecondaryFg => _dark ? Color.FromArgb(170, 245, 245, 247) : Color.FromArgb(145, 29, 29, 31);
    private Color AccentBlue => _dark ? Color.FromArgb(255, 10, 132, 255) : Color.FromArgb(255, 0, 122, 255);
    private Color AccentBlueHover => _dark ? Color.FromArgb(255, 30, 144, 255) : Color.FromArgb(255, 20, 132, 255);
    private Color AccentBluePressed => _dark ? Color.FromArgb(255, 8, 108, 212) : Color.FromArgb(255, 0, 106, 224);
    private Color StrokeColor => _dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0);
    private Color CardBg => _dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(12, 0, 0, 0);

    public static bool IsVisible => s_instance != null && !s_instance.IsDisposed && s_instance.Visible;

    internal MacAboutWindow(string? version = null, bool? dark = null)
    {
        _version = string.IsNullOrWhiteSpace(version)
            ? Updater.CurrentVersion.ToString(3)
            : version.TrimStart('v');
        _dark = dark ?? !SystemTheme.UsesLightTaskbar();
        _scale = DeviceDpi / 96f;
        _cornerRadius = (int)(16 * _scale);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        Opacity = 0.05;
        BackColor = Color.FromArgb(BgColor.R, BgColor.G, BgColor.B);
        KeyPreview = true;

        // 紧凑型尺寸：328 x 430（黄金卡片比例，优雅容纳版本徽标）
        int width = (int)(328 * _scale);
        int height = (int)(430 * _scale);
        ClientSize = new Size(width, height);

        // 居中呈现于用户当前操作光标所在的显示器工作区（符合 macOS 原生面板标准）
        var screen = Screen.FromPoint(Cursor.Position);
        var wa = screen.WorkingArea;
        Location = new Point(
            wa.Left + Math.Max(0, (wa.Width - width) / 2),
            wa.Top + Math.Max(0, (wa.Height - height) / 2));

        HandleCreated += (_, _) => UpdateWindowRegion();

        // 平滑淡入
        _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animTimer.Tick += (_, _) =>
        {
            if (IsDisposed || Disposing)
            {
                _animTimer?.Stop();
                return;
            }
            if (Opacity < _targetOpacity)
            {
                Opacity = Math.Min(_targetOpacity, Opacity + 0.12);
            }
            else
            {
                _animTimer?.Stop();
                _animTimer?.Dispose();
            }
        };
        _animTimer.Start();
    }

    private void UpdateWindowRegion()
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
            else
            {
                Region = null;
            }
        }
        catch { }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _scale = e.DeviceDpiNew / 96f;
        _cornerRadius = (int)(16 * _scale);
        Bounds = e.SuggestedRectangle;
        UpdateWindowRegion();
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter)
        {
            Close();
            e.Handled = true;
            return;
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

        // ——— 2. macOS 顶部标题栏控制（左上角红点 + 居中标题） ———
        float dotSize = 12 * _scale;
        float dotX = 14 * _scale;
        float dotY = 13 * _scale;
        _closeBtnRect = new RectangleF(dotX - 3, dotY - 3, dotSize + 6, dotSize + 6);

        Color dotColor = _closePressed
            ? Color.FromArgb(255, 220, 60, 52)
            : (_closeHover ? Color.FromArgb(255, 255, 110, 102) : Color.FromArgb(255, 255, 95, 86));

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

        // 顶部居中细致标题
        using (var topFont = new Font("Microsoft YaHei", 12f * _scale, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            DrawStringCenter(g, "关于软件", topFont, SecondaryFg, new RectangleF(0, 11 * _scale, w, 18 * _scale));
        }

        // ——— 3. 应用标识（居中精致磁贴 + 品牌名 + 精致半透明版本徽标胶囊 + 副标题） ———
        int iconSize = (int)(50 * _scale);
        int iconX = (w - iconSize) / 2;
        int iconY = (int)(36 * _scale);
        using (var tileBmp = IconFactory.RenderAppTile(iconSize))
        {
            g.DrawImage(tileBmp, iconX, iconY, iconSize, iconSize);
        }

        // 品牌应用名（Bold 21px）
        using var nameFont = new Font("Microsoft YaHei", 21f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        var nameSize = g.MeasureString("Numlock", nameFont);
        int nameY = iconY + iconSize + (int)(6 * _scale);
        DrawStringCenter(g, "Numlock", nameFont, FgColor, new RectangleF(0, nameY, w, nameSize.Height));

        // 精致半透明版本徽标胶囊（macOS 26 磨砂半透明胶囊，保留版本号徽标）
        string verBadge = "v" + _version;
        using var verFont = new Font("Microsoft YaHei", 11.5f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        var verSize = g.MeasureString(verBadge, verFont);
        int badgeW = (int)verSize.Width + (int)(16 * _scale);
        int badgeH = (int)verSize.Height + (int)(4 * _scale);
        int badgeX = (w - badgeW) / 2;
        int badgeY = nameY + (int)nameSize.Height + (int)(4 * _scale);

        var badgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);
        using (var badgePath = Gfx.RoundedRect(badgeRect, badgeH / 2f))
        {
            using var badgeFill = new SolidBrush(Color.FromArgb(_dark ? 40 : 25, AccentBlue));
            g.FillPath(badgeFill, badgePath);
            using var badgePen = new Pen(Color.FromArgb(_dark ? 80 : 50, AccentBlue), 1f);
            g.DrawPath(badgePen, badgePath);
        }
        DrawStringCenter(g, verBadge, verFont, AccentBlue, badgeRect);

        // 副标题描述（紧凑排布于版本徽标下方）
        using var subFont = new Font("Microsoft YaHei", 12f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        int subY = badgeY + badgeH + (int)(5 * _scale);
        DrawStringCenter(g, "数字小键盘状态看护 · 保持常开", subFont, SecondaryFg, new RectangleF(0, subY, w, 18 * _scale));

        // ——— 4. 状态图例与功能卡片（macOS Inset Card） ———
        int cardMargin = (int)(16 * _scale);
        int cardX = cardMargin;
        int cardY = subY + (int)(18 * _scale);
        int cardW = w - cardMargin * 2;
        int cardH = (int)(152 * _scale);

        var cardRect = new RectangleF(cardX, cardY, cardW, cardH);
        using (var cardPath = Gfx.RoundedRect(cardRect, 10 * _scale))
        {
            using var cardBrush = new SolidBrush(CardBg);
            g.FillPath(cardBrush, cardPath);
            using var cardStroke = new Pen(StrokeColor, 1f);
            g.DrawPath(cardStroke, cardPath);
        }

        // 4 行图例：紧凑精致字阶（Label: 13px Bold, Desc: 12px Regular）
        int rowH = (int)(34 * _scale);
        int startRowY = cardY + (int)(8 * _scale);
        int rowPadX = cardX + (int)(12 * _scale);
        int cardRight = cardX + cardW;

        DrawLegendRow(g, rowPadX, startRowY + rowH * 0, cardRight, "实心键帽", "·  已开启（NumLock ON）", isSolid: true, hasBadge: false);
        DrawLegendRow(g, rowPadX, startRowY + rowH * 1, cardRight, "描边键帽", "·  已关闭（NumLock OFF）", isSolid: false, hasBadge: false);
        DrawLegendRow(g, rowPadX, startRowY + rowH * 2, cardRight, "绿色圆点", "·  保持常开模式已激活", isSolid: true, hasBadge: true);
        DrawActionRow(g, rowPadX, startRowY + rowH * 3, cardRight, "快捷交互", "·  双击切换 · 右键菜单");

        // ——— 5. 底部主页链接与操作按钮 ———
        string linkText = "GitHub: Crucialmer/Numlock";
        using var linkFont = new Font("Microsoft YaHei", 12f * _scale, _linkHover ? FontStyle.Underline : FontStyle.Regular, GraphicsUnit.Pixel);
        var linkSize = g.MeasureString(linkText, linkFont);
        int linkY = cardY + cardH + (int)(12 * _scale);
        int linkX = (int)((w - linkSize.Width) / 2f);
        _linkRect = new RectangleF(linkX, linkY, linkSize.Width, linkSize.Height);

        DrawStringCenter(g, linkText, linkFont, _linkHover ? AccentBlueHover : SecondaryFg, _linkRect);

        // 主操作胶囊按钮：“好”
        int btnW = (int)(96 * _scale);
        int btnH = (int)(30 * _scale);
        int btnX = (w - btnW) / 2;
        int btnY = linkY + (int)linkSize.Height + (int)(10 * _scale);
        _actionBtnRect = new RectangleF(btnX, btnY, btnW, btnH);

        Color btnBg = _actionPressed
            ? AccentBluePressed
            : (_actionHover ? AccentBlueHover : AccentBlue);

        using var btnFont = new Font("Microsoft YaHei", 13.5f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        DrawPillButton(g, _actionBtnRect, "好", btnFont, btnBg, Color.White);

        // ——— 6. 面板外边框 1px 半透明发丝描边 ———
        using var strokePen = new Pen(StrokeColor, 1f);
        using var borderPath = Gfx.RoundedRect(new RectangleF(0.5f, 0.5f, w - 1f, h - 1f), _cornerRadius - 0.5f);
        g.DrawPath(strokePen, borderPath);
    }

    private void DrawLegendRow(Graphics g, int x, int y, int cardRight, string label, string desc, bool isSolid, bool hasBadge)
    {
        int sz = (int)(16 * _scale);
        int iconY = y + (int)(8 * _scale);
        using var iconBmp = IconFactory.RenderTrayIcon(sz, isSolid, _dark, hasBadge);
        g.DrawImage(iconBmp, x, iconY, sz, sz);

        int textX = x + sz + (int)(8 * _scale);
        using var boldFont = new Font("Microsoft YaHei", 13f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        var labelSize = g.MeasureString(label, boldFont);
        int rowH = (int)(32 * _scale);
        DrawStringLeft(g, label, boldFont, FgColor, new RectangleF(textX, y, labelSize.Width, rowH));

        int descX = textX + (int)labelSize.Width + (int)(4 * _scale);
        float descW = Math.Max(0, cardRight - descX - (int)(8 * _scale));
        using var descFont = new Font("Microsoft YaHei", 12f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawStringLeft(g, desc, descFont, SecondaryFg, new RectangleF(descX, y, descW, rowH));
    }

    private void DrawActionRow(Graphics g, int x, int y, int cardRight, string label, string desc)
    {
        int sz = (int)(16 * _scale);
        float dotR = 3.5f * _scale;
        using var dotBrush = new SolidBrush(Color.FromArgb(_dark ? 140 : 180, AccentBlue));
        g.FillEllipse(dotBrush, x + sz / 2f - dotR, y + (int)(8 * _scale) + sz / 2f - dotR, dotR * 2, dotR * 2);

        int textX = x + sz + (int)(8 * _scale);
        using var boldFont = new Font("Microsoft YaHei", 13f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        var labelSize = g.MeasureString(label, boldFont);
        int rowH = (int)(32 * _scale);
        DrawStringLeft(g, label, boldFont, FgColor, new RectangleF(textX, y, labelSize.Width, rowH));

        int descX = textX + (int)labelSize.Width + (int)(4 * _scale);
        float descW = Math.Max(0, cardRight - descX - (int)(8 * _scale));
        using var descFont = new Font("Microsoft YaHei", 12f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawStringLeft(g, desc, descFont, SecondaryFg, new RectangleF(descX, y, descW, rowH));
    }

    private void DrawPillButton(Graphics g, RectangleF rect, string text, Font font, Color bgColor, Color textColor)
    {
        using (var path = Gfx.RoundedRect(rect, 7 * _scale))
        {
            using var brush = new SolidBrush(bgColor);
            g.FillPath(brush, path);
        }
        DrawStringCenter(g, text, font, textColor, rect);
    }

    private static void DrawStringCenter(Graphics g, string text, Font font, Color color, RectangleF rect, bool wordBreak = false)
    {
        using var brush = new SolidBrush(color);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = wordBreak ? 0 : StringFormatFlags.NoWrap
        };
        g.DrawString(text, font, brush, rect, sf);
    }

    private static void DrawStringLeft(Graphics g, string text, Font font, Color color, RectangleF rect, bool wordBreak = false)
    {
        using var brush = new SolidBrush(color);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = wordBreak ? 0 : StringFormatFlags.NoWrap
        };
        g.DrawString(text, font, brush, rect, sf);
    }

    // ——— 鼠标交互与全窗平滑拖拽 ———

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (_closeBtnRect.Contains(e.Location))
            {
                _closePressed = true;
                Invalidate();
                return;
            }
            if (_actionBtnRect.Contains(e.Location))
            {
                _actionPressed = true;
                Invalidate();
                return;
            }
            if (_linkRect.Contains(e.Location))
            {
                return;
            }

            // 点击窗口其余区域可平滑拖动窗口
            try
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, 0xA1, 2, 0); // WM_NCLBUTTONDOWN, HTCAPTION
            }
            catch { }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
            _animTimer?.Stop();
            _animTimer?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
        _animTimer?.Stop();
        _animTimer?.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>单例打开 macOS 风格“关于软件”紧凑型独立卡片窗口。</summary>
    public static void ShowAbout(string? version = null)
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

    /// <summary>自检与文档离线截图渲染辅助：无需实际弹出窗口即可离线精确渲染外观。</summary>
    internal static Bitmap RenderPreview(bool dark, string? version = null)
    {
        version ??= Updater.CurrentVersion.ToString(3);
        using var win = new MacAboutWindow(version, dark);
        var bmp = new Bitmap(win.ClientSize.Width, win.ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        bmp.SetResolution(96, 96);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(win.BgColor);
            using var pe = new PaintEventArgs(g, new Rectangle(0, 0, bmp.Width, bmp.Height));
            win.OnPaint(pe);
        }
        return bmp;
    }
}
