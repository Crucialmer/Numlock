using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Numlock;

/// <summary>
/// macOS 26 风格对话框：替代原生 MessageBox，提供与 macOS 一致的视觉体验。
/// · 连续曲率圆角（Squircle）窗口
/// · 半透明毛玻璃材质底色（DWM 亚克力 + 半透明底色）
/// · macOS 风格按钮（圆角蓝色主按钮 + 灰色辅助按钮）
/// · 图标使用 SF Symbols 风格自绘
/// · 高 DPI 适配（100%–400%）
/// </summary>
internal sealed class MacDialog : Form
{
    /// <summary>对话框类型（决定图标和配色）。</summary>
    internal enum DialogIcon { Info, Warning, Error }

    /// <summary>按钮布局。</summary>
    internal enum DialogButtons { Ok, OkCancel, YesNo }

    private readonly string _title;
    private readonly string _message;
    private readonly DialogIcon _icon;
    private readonly DialogButtons _buttons;
    private readonly bool _dark;

    // 绘制缓存
    private float _scale;
    private int _iconSize;
    private int _btnHeight;
    private int _btnMinWidth;
    private int _padding;
    private int _cornerRadius;
    private int _titleFontSize;
    private int _bodyFontSize;

    private bool _primaryHover;
    private bool _primaryPressed;
    private bool _secondaryHover;
    private bool _secondaryPressed;
    private Rectangle _primaryBtnRect;
    private Rectangle _secondaryBtnRect;
    private bool _hasSecondary;

    // macOS 26 配色（与 MacMenuRenderer 一致）
    private Color BgColor => _dark ? Color.FromArgb(235, 36, 36, 38) : Color.FromArgb(235, 246, 246, 248);
    private Color FgColor => _dark ? Color.FromArgb(255, 245, 245, 247) : Color.FromArgb(255, 29, 29, 31);
    private Color SecondaryFg => _dark ? Color.FromArgb(180, 245, 245, 247) : Color.FromArgb(160, 29, 29, 31);
    private Color AccentBlue => _dark ? Color.FromArgb(255, 10, 132, 255) : Color.FromArgb(255, 0, 122, 255);
    private Color AccentBlueHover => _dark ? Color.FromArgb(255, 30, 144, 255) : Color.FromArgb(255, 20, 132, 255);
    private Color AccentBluePressed => _dark ? Color.FromArgb(255, 8, 108, 212) : Color.FromArgb(255, 0, 106, 224);
    private Color SecondaryBtnBg => _dark ? Color.FromArgb(255, 62, 62, 66) : Color.FromArgb(255, 230, 230, 234);
    private Color SecondaryBtnHover => _dark ? Color.FromArgb(255, 72, 72, 78) : Color.FromArgb(255, 220, 220, 226);
    private Color SecondaryBtnPressed => _dark ? Color.FromArgb(255, 52, 52, 56) : Color.FromArgb(255, 210, 210, 214);
    private Color StrokeColor => _dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0);
    private Color SeparatorColor => _dark ? Color.FromArgb(36, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);

    // 图标配色
    private Color InfoIconColor => Color.FromArgb(255, 10, 132, 255);
    private Color WarningIconColor => Color.FromArgb(255, 255, 179, 0);
    private Color ErrorIconColor => Color.FromArgb(255, 255, 69, 58);

    private MacDialog(string title, string message, DialogIcon icon, DialogButtons buttons)
    {
        _title = title;
        _message = message;
        _icon = icon;
        _buttons = buttons;
        _dark = !SystemTheme.UsesLightTaskbar();
        _hasSecondary = buttons != DialogButtons.Ok;

        _scale = DeviceDpi / 96f;
        UpdateMetrics();
        SetupForm();
    }

    private void UpdateMetrics()
    {
        _iconSize = (int)(44 * _scale);
        _btnHeight = (int)(32 * _scale);
        _btnMinWidth = (int)(76 * _scale);
        _padding = (int)(24 * _scale);
        _cornerRadius = (int)(14 * _scale);
        _titleFontSize = (int)(17 * _scale);
        _bodyFontSize = (int)(13.5f * _scale);
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
        UpdateMetrics();
        Bounds = e.SuggestedRectangle;
        UpdateWindowRegion();
        Invalidate();
    }

    private void SetupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(BgColor.R, BgColor.G, BgColor.B);
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;

        // 测量文字计算窗口尺寸
        int dialogWidth = (int)(380 * _scale);
        int contentWidth = dialogWidth - _padding * 2;

        using var titleFont = new Font("Microsoft YaHei", _titleFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Microsoft YaHei", _bodyFontSize, FontStyle.Regular, GraphicsUnit.Pixel);

        int titleHeight;
        int bodyHeight;
        using (var g = CreateGraphics())
        {
            var titleSize = g.MeasureString(_title, titleFont, contentWidth);
            titleHeight = (int)Math.Ceiling(titleSize.Height);

            var bodySize = g.MeasureString(_message, bodyFont, contentWidth);
            bodyHeight = (int)Math.Ceiling(bodySize.Height);
        }

        int totalHeight = _padding                   // 顶部留白
                        + _iconSize                  // 图标
                        + (int)(12 * _scale)         // 图标与标题间距
                        + titleHeight                // 标题
                        + (int)(8 * _scale)          // 标题与正文间距
                        + bodyHeight                 // 正文
                        + (int)(20 * _scale)         // 正文与分隔线间距
                        + 1                          // 分隔线
                        + (int)(16 * _scale)         // 分隔线与按钮间距
                        + _btnHeight                 // 按钮
                        + _padding;                  // 底部留白

        ClientSize = new Size(dialogWidth, totalHeight);

        // DWM 圆角 + 深色模式 + 亚克力
        HandleCreated += (_, _) => UpdateWindowRegion();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
        {
            DialogResult = e.KeyCode == Keys.Escape && _hasSecondary
                ? DialogResult.Cancel
                : DialogResult.OK;
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

        int dialogWidth = ClientSize.Width;
        int contentWidth = dialogWidth - _padding * 2;

        // ——— 背景 ———
        using (var bgBrush = new SolidBrush(BgColor))
        {
            g.FillRectangle(bgBrush, ClientRectangle);
        }

        int y = _padding;

        // ——— 图标（居中） ———
        int iconX = (dialogWidth - _iconSize) / 2;
        DrawMacIcon(g, new Rectangle(iconX, y, _iconSize, _iconSize));
        y += _iconSize + (int)(12 * _scale);

        // ——— 标题（居中） ———
        using var titleFont = new Font("Microsoft YaHei", _titleFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        var titleSize = g.MeasureString(_title, titleFont, contentWidth);
        var titleRect = new RectangleF(_padding, y, contentWidth, titleSize.Height);
        DrawStringCenter(g, _title, titleFont, FgColor, titleRect, wordBreak: true);
        y += (int)Math.Ceiling(titleSize.Height) + (int)(8 * _scale);

        // ——— 正文（居中） ———
        using var bodyFont = new Font("Microsoft YaHei", _bodyFontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        var bodySize = g.MeasureString(_message, bodyFont, contentWidth);
        var bodyRect = new RectangleF(_padding, y, contentWidth, bodySize.Height);
        DrawStringCenter(g, _message, bodyFont, SecondaryFg, bodyRect, wordBreak: true);
        y += (int)Math.Ceiling(bodySize.Height) + (int)(20 * _scale);

        // ——— 分隔线 ———
        using (var separatorPen = new Pen(SeparatorColor, 1f))
        {
            g.DrawLine(separatorPen, _padding, y, dialogWidth - _padding, y);
        }
        y += 1 + (int)(16 * _scale);

        // ——— 按钮 ———
        DrawButtons(g, y, dialogWidth);

        // ——— 边框描边 ———
        using var strokePen = new Pen(StrokeColor, 1f);
        using var borderPath = Gfx.RoundedRect(
            new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f),
            _cornerRadius - 0.5f);
        g.DrawPath(strokePen, borderPath);
    }

    private void DrawButtons(Graphics g, int y, int dialogWidth)
    {
        using var btnFont = new Font("Microsoft YaHei", (int)(14 * _scale), FontStyle.Bold, GraphicsUnit.Pixel);

        string primaryText = _buttons switch
        {
            DialogButtons.YesNo => "是",
            _ => "好",
        };
        string secondaryText = _buttons switch
        {
            DialogButtons.YesNo => "否",
            DialogButtons.OkCancel => "取消",
            _ => "",
        };

        int spacing = (int)(10 * _scale);
        int btnCorner = (int)(7 * _scale);

        // 测量按钮宽度
        int primaryW = Math.Max(_btnMinWidth,
            (int)g.MeasureString(primaryText, btnFont).Width + (int)(28 * _scale));

        if (_hasSecondary)
        {
            int secondaryW = Math.Max(_btnMinWidth,
                (int)g.MeasureString(secondaryText, btnFont).Width + (int)(28 * _scale));
            int totalW = primaryW + spacing + secondaryW;
            int startX = (dialogWidth - totalW) / 2;

            // 辅助按钮（左）
            _secondaryBtnRect = new Rectangle(startX, y, secondaryW, _btnHeight);
            DrawButton(g, _secondaryBtnRect, secondaryText, btnFont, btnCorner,
                false, _secondaryHover, _secondaryPressed);

            // 主按钮（右）
            _primaryBtnRect = new Rectangle(startX + secondaryW + spacing, y, primaryW, _btnHeight);
            DrawButton(g, _primaryBtnRect, primaryText, btnFont, btnCorner,
                true, _primaryHover, _primaryPressed);
        }
        else
        {
            int startX = (dialogWidth - primaryW) / 2;
            _primaryBtnRect = new Rectangle(startX, y, primaryW, _btnHeight);
            DrawButton(g, _primaryBtnRect, primaryText, btnFont, btnCorner,
                true, _primaryHover, _primaryPressed);
        }
    }

    private void DrawButton(Graphics g, Rectangle rect, string text, Font font, int cornerRadius,
        bool isPrimary, bool hover, bool pressed)
    {
        Color bg;
        Color fg;
        if (isPrimary)
        {
            bg = pressed ? AccentBluePressed : (hover ? AccentBlueHover : AccentBlue);
            fg = Color.White;
        }
        else
        {
            bg = pressed ? SecondaryBtnPressed : (hover ? SecondaryBtnHover : SecondaryBtnBg);
            fg = FgColor;
        }

        var btnRect = new RectangleF(rect.X, rect.Y, rect.Width, rect.Height);
        using var path = Gfx.RoundedRect(btnRect, cornerRadius);
        using var brush = new SolidBrush(bg);
        g.FillPath(brush, path);

        // 按钮文字
        DrawStringCenter(g, text, font, fg, btnRect);
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

    /// <summary>绘制 macOS 26 风格图标（自绘矢量，不依赖外部资源）。</summary>
    private void DrawMacIcon(Graphics g, Rectangle bounds)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = bounds.X + bounds.Width / 2f;
        float cy = bounds.Y + bounds.Height / 2f;
        float r = bounds.Width / 2f;

        switch (_icon)
        {
            case DialogIcon.Info:
                DrawInfoIcon(g, cx, cy, r);
                break;
            case DialogIcon.Warning:
                DrawWarningIcon(g, cx, cy, r);
                break;
            case DialogIcon.Error:
                DrawErrorIcon(g, cx, cy, r);
                break;
        }
    }

    private void DrawInfoIcon(Graphics g, float cx, float cy, float r)
    {
        // 圆形底
        using var bgPath = Gfx.RoundedRect(
            new RectangleF(cx - r, cy - r, r * 2, r * 2), r);
        using var bgBrush = new SolidBrush(InfoIconColor);
        g.FillPath(bgBrush, bgPath);

        // "i" 字母
        float penW = Math.Max(2f, r * 0.14f);
        using var pen = new Pen(Color.White, penW) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        // 圆点
        float dotR = penW * 0.85f;
        using var dotBrush = new SolidBrush(Color.White);
        g.FillEllipse(dotBrush, cx - dotR, cy - r * 0.45f - dotR, dotR * 2, dotR * 2);
        // 竖线
        g.DrawLine(pen, cx, cy - r * 0.15f, cx, cy + r * 0.48f);
    }

    private void DrawWarningIcon(Graphics g, float cx, float cy, float r)
    {
        // 三角形底
        float triH = r * 1.75f;
        float triW = triH * 1.15f;
        PointF[] tri =
        [
            new(cx, cy - triH / 2f + r * 0.08f),
            new(cx - triW / 2f, cy + triH / 2f - r * 0.08f),
            new(cx + triW / 2f, cy + triH / 2f - r * 0.08f),
        ];
        using var bgBrush = new SolidBrush(WarningIconColor);
        g.FillPolygon(bgBrush, tri);

        // "!" 符号
        float penW = Math.Max(2f, r * 0.13f);
        using var pen = new Pen(Color.White, penW) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, cx, cy - r * 0.28f, cx, cy + r * 0.18f);
        float dotR = penW * 0.8f;
        using var dotBrush = new SolidBrush(Color.White);
        g.FillEllipse(dotBrush, cx - dotR, cy + r * 0.36f - dotR, dotR * 2, dotR * 2);
    }

    private void DrawErrorIcon(Graphics g, float cx, float cy, float r)
    {
        // 圆形底
        using var bgPath = Gfx.RoundedRect(
            new RectangleF(cx - r, cy - r, r * 2, r * 2), r);
        using var bgBrush = new SolidBrush(ErrorIconColor);
        g.FillPath(bgBrush, bgPath);

        // "×" 符号
        float penW = Math.Max(2f, r * 0.16f);
        float crossR = r * 0.35f;
        using var pen = new Pen(Color.White, penW) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, cx - crossR, cy - crossR, cx + crossR, cy + crossR);
        g.DrawLine(pen, cx + crossR, cy - crossR, cx - crossR, cy + crossR);
    }

    // ——— 鼠标交互（拖拽 + 按钮悬停/点击） ———

    private bool _dragging;
    private Point _dragStart;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (_primaryBtnRect.Contains(e.Location))
            {
                _primaryPressed = true;
                Invalidate(_primaryBtnRect);
            }
            else if (_hasSecondary && _secondaryBtnRect.Contains(e.Location))
            {
                _secondaryPressed = true;
                Invalidate(_secondaryBtnRect);
            }
            else
            {
                _dragging = true;
                _dragStart = e.Location;
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_primaryPressed && _primaryBtnRect.Contains(e.Location))
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        if (_secondaryPressed && _hasSecondary && _secondaryBtnRect.Contains(e.Location))
        {
            DialogResult = _buttons == DialogButtons.YesNo ? DialogResult.No : DialogResult.Cancel;
            Close();
            return;
        }

        _primaryPressed = false;
        _secondaryPressed = false;
        _dragging = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            Location = new Point(
                Location.X + e.X - _dragStart.X,
                Location.Y + e.Y - _dragStart.Y);
            return;
        }

        bool ph = _primaryBtnRect.Contains(e.Location);
        bool sh = _hasSecondary && _secondaryBtnRect.Contains(e.Location);
        if (ph != _primaryHover || sh != _secondaryHover)
        {
            _primaryHover = ph;
            _secondaryHover = sh;
            Cursor = (ph || sh) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_primaryHover || _secondaryHover)
        {
            _primaryHover = false;
            _secondaryHover = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    // ——— 静态公共 API（替代 MessageBox） ———

    /// <summary>显示 macOS 26 风格的信息对话框（单个"好"按钮）。</summary>
    public static DialogResult ShowInfo(string title, string message)
        => ShowDialog(title, message, DialogIcon.Info, DialogButtons.Ok);

    /// <summary>显示 macOS 26 风格的警告对话框。</summary>
    public static DialogResult ShowWarning(string title, string message)
        => ShowDialog(title, message, DialogIcon.Warning, DialogButtons.Ok);

    /// <summary>显示 macOS 26 风格的警告确认对话框（"好"+"取消"）。</summary>
    public static DialogResult ShowWarningOkCancel(string title, string message)
        => ShowDialog(title, message, DialogIcon.Warning, DialogButtons.OkCancel);

    /// <summary>显示 macOS 26 风格的错误对话框。</summary>
    public static DialogResult ShowError(string title, string message)
        => ShowDialog(title, message, DialogIcon.Error, DialogButtons.Ok);

    private static DialogResult ShowDialog(string title, string message, DialogIcon icon, DialogButtons buttons)
    {
        try
        {
            using var dialog = new MacDialog(title, message, icon, buttons);
            return dialog.ShowDialog();
        }
        catch
        {
            // 自绘对话框失败时降级到原生 MessageBox（绝不让提示本身成为故障点）
            var mbIcon = icon switch
            {
                DialogIcon.Warning => MessageBoxIcon.Warning,
                DialogIcon.Error => MessageBoxIcon.Error,
                _ => MessageBoxIcon.Information,
            };
            var mbButtons = buttons switch
            {
                DialogButtons.OkCancel => MessageBoxButtons.OKCancel,
                DialogButtons.YesNo => MessageBoxButtons.YesNo,
                _ => MessageBoxButtons.OK,
            };
            return MessageBox.Show(message, title, mbButtons, mbIcon);
        }
    }

    /// <summary>显示 macOS 26 风格的"关于"对话框。</summary>
    public static void ShowAbout(string version)
    {
        ShowInfo(
            "关于 Numlock v" + version,
            "在系统托盘显示数字小键盘（NumLock）的开关状态。\n\n" +
            "· 实心键帽 = 已开启\n" +
            "· 描边键帽 = 已关闭\n" +
            "· 右下角绿点 = 保持常开模式\n\n" +
            "双击托盘图标快速切换 NumLock\n" +
            "右键打开菜单\n\n" +
            "项目主页\nhttps://github.com/Crucialmer/Numlock");
    }
}
