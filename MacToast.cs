using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Numlock;

/// <summary>
/// macOS 26 风格右下角浮动通知与更新卡片：
/// · 严格位于屏幕右下角工作区
/// · 连续曲率圆角矩形（Squircle）+ 极细半透明描边
/// · DWM 瞬态亚克力材质 + 沉浸式深/浅双主题
/// · 丰富交互：支持“正在检查”、“已是最新”、“重启并更新”、“查看发布页”等按钮回调
/// · 不夺取前台输入焦点（ShowWithoutActivation），丝滑不打扰用户
/// </summary>
internal sealed class MacToast : Form
{
    internal enum ToastType
    {
        Info,
        Success,
        Warning,
        Error,
        Checking,
        UpdateReady
    }

    private static MacToast? s_current;

    private readonly ToastType _type;
    private readonly string _title;
    private readonly string _message;
    private readonly string _primaryBtnText;
    private readonly string _secondaryBtnText;
    private readonly Action? _primaryAction;
    private readonly Action? _secondaryAction;
    private readonly bool _hasButtons;
    private readonly bool _dark;
    private readonly float _scale;

    private readonly int _cornerRadius;
    private RectangleF _closeBtnRect;
    private RectangleF _primaryBtnRect;
    private RectangleF _secondaryBtnRect;

    private bool _closeHover;
    private bool _primaryHover;
    private bool _primaryPressed;
    private bool _secondaryHover;
    private bool _secondaryPressed;

    private readonly System.Windows.Forms.Timer? _autoCloseTimer;
    private readonly System.Windows.Forms.Timer? _animTimer;
    private double _targetOpacity = 1.0;

    // macOS 26 配色
    private Color BgColor => _dark ? Color.FromArgb(232, 34, 34, 38) : Color.FromArgb(238, 246, 246, 248);
    private Color FgColor => _dark ? Color.FromArgb(255, 245, 245, 247) : Color.FromArgb(255, 29, 29, 31);
    private Color SecondaryFg => _dark ? Color.FromArgb(170, 245, 245, 247) : Color.FromArgb(150, 29, 29, 31);
    private Color AccentBlue => _dark ? Color.FromArgb(255, 10, 132, 255) : Color.FromArgb(255, 0, 122, 255);
    private Color AccentBlueHover => _dark ? Color.FromArgb(255, 30, 144, 255) : Color.FromArgb(255, 20, 132, 255);
    private Color AccentBluePressed => _dark ? Color.FromArgb(255, 8, 108, 212) : Color.FromArgb(255, 0, 106, 224);
    private Color SecondaryBtnBg => _dark ? Color.FromArgb(255, 58, 58, 62) : Color.FromArgb(255, 226, 226, 230);
    private Color SecondaryBtnHover => _dark ? Color.FromArgb(255, 70, 70, 76) : Color.FromArgb(255, 216, 216, 222);
    private Color SecondaryBtnPressed => _dark ? Color.FromArgb(255, 48, 48, 52) : Color.FromArgb(255, 206, 206, 212);
    private Color StrokeColor => _dark ? Color.FromArgb(42, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);

    // 图标色彩
    private Color InfoColor => Color.FromArgb(255, 10, 132, 255);
    private Color SuccessColor => Color.FromArgb(255, 48, 209, 88);
    private Color WarningColor => Color.FromArgb(255, 255, 179, 0);
    private Color ErrorColor => Color.FromArgb(255, 255, 69, 58);

    protected override bool ShowWithoutActivation => true;

    private MacToast(
        string title,
        string message,
        ToastType type,
        string primaryBtnText = "",
        Action? primaryAction = null,
        string secondaryBtnText = "",
        Action? secondaryAction = null,
        int autoCloseMs = 4000)
    {
        _title = title;
        _message = message;
        _type = type;
        _primaryBtnText = primaryBtnText;
        _primaryAction = primaryAction;
        _secondaryBtnText = secondaryBtnText;
        _secondaryAction = secondaryAction;
        _hasButtons = !string.IsNullOrEmpty(primaryBtnText);
        _dark = !SystemTheme.UsesLightTaskbar();
        _scale = DeviceDpi / 96f;
        _cornerRadius = (int)(14 * _scale);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        Opacity = 0.05;
        BackColor = Color.FromArgb(BgColor.R, BgColor.G, BgColor.B);

        int width = (int)(336 * _scale);
        int baseHeight = _hasButtons ? (int)(126 * _scale) : (int)(96 * _scale);
        ClientSize = new Size(width, baseHeight);

        // 位于屏幕右下角（任务栏工作区边缘留白）
        var screen = Screen.FromPoint(Cursor.Position);
        var wa = screen.WorkingArea;
        int margin = (int)(16 * _scale);
        Location = new Point(wa.Right - width - margin, wa.Bottom - baseHeight - margin);

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

        // 淡入动画
        _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animTimer.Tick += (_, _) =>
        {
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

        // 自动关闭（无交互按钮时）
        if (!_hasButtons && autoCloseMs > 0)
        {
            _autoCloseTimer = new System.Windows.Forms.Timer { Interval = autoCloseMs };
            _autoCloseTimer.Tick += (_, _) =>
            {
                _autoCloseTimer?.Stop();
                _autoCloseTimer?.Dispose();
                Close();
            };
            _autoCloseTimer.Start();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        int w = ClientSize.Width;
        int h = ClientSize.Height;

        // 1. 面板背景
        using (var bgBrush = new SolidBrush(BgColor))
        {
            g.FillRectangle(bgBrush, ClientRectangle);
        }

        // 2. 顶部微型标头（应用图标 + 名称 + 分类 + 右上角关闭叉）
        int padX = (int)(14 * _scale);
        int topY = (int)(10 * _scale);

        int miniIconSize = (int)(14 * _scale);
        using (var miniTile = IconFactory.RenderAppTile(miniIconSize))
        {
            g.DrawImage(miniTile, padX, topY, miniIconSize, miniIconSize);
        }

        using var appNameFont = new Font("Segoe UI", 10.5f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        var appNameSize = TextRenderer.MeasureText(g, "Numlock", appNameFont);
        TextRenderer.DrawText(g, "Numlock", appNameFont,
            new Rectangle(padX + miniIconSize + (int)(6 * _scale), topY - 1, appNameSize.Width, miniIconSize + 2),
            FgColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        string catText = _type switch
        {
            ToastType.Checking or ToastType.UpdateReady => "检查更新",
            _ => "系统通知",
        };
        using var catFont = new Font("Segoe UI", 10f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        int catX = padX + miniIconSize + (int)(6 * _scale) + appNameSize.Width + (int)(6 * _scale);
        TextRenderer.DrawText(g, "· " + catText, catFont,
            new Rectangle(catX, topY - 1, (int)(100 * _scale), miniIconSize + 2),
            SecondaryFg, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        // 右上角小关闭按钮
        int closeBtnSize = (int)(14 * _scale);
        int closeX = w - padX - closeBtnSize;
        int closeY = topY;
        _closeBtnRect = new RectangleF(closeX - 4, closeY - 4, closeBtnSize + 8, closeBtnSize + 8);

        using (var closePen = new Pen(_closeHover ? FgColor : SecondaryFg, 1.3f * _scale))
        {
            float cx = closeX + closeBtnSize / 2f;
            float cy = closeY + closeBtnSize / 2f;
            float r = closeBtnSize * 0.35f;
            g.DrawLine(closePen, cx - r, cy - r, cx + r, cy + r);
            g.DrawLine(closePen, cx + r, cy - r, cx - r, cy + r);
        }

        // 3. 图标（左侧）
        int bodyY = topY + miniIconSize + (int)(10 * _scale);
        int iconSize = (int)(28 * _scale);
        DrawStatusIcon(g, padX, bodyY, iconSize);

        // 4. 文字（右侧）
        int textX = padX + iconSize + (int)(12 * _scale);
        int textW = w - textX - padX;

        using var titleFont = new Font("Segoe UI", 12 * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        var titleSize = TextRenderer.MeasureText(g, _title, titleFont);
        TextRenderer.DrawText(g, _title, titleFont,
            new Rectangle(textX, bodyY - (int)(1 * _scale), textW, titleSize.Height),
            FgColor, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.WordEllipsis);

        int msgY = bodyY + titleSize.Height + (int)(2 * _scale);
        using var msgFont = new Font("Segoe UI", 11 * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, _message, msgFont,
            new Rectangle(textX, msgY, textW, (int)(32 * _scale)),
            SecondaryFg, TextFormatFlags.Left | TextFormatFlags.WordBreak);

        // 5. 交互按钮（底部）
        if (_hasButtons)
        {
            int btnH = (int)(26 * _scale);
            int btnY = h - (int)(12 * _scale) - btnH;
            using var btnFont = new Font("Segoe UI", 11 * _scale, FontStyle.Regular, GraphicsUnit.Pixel);

            if (!string.IsNullOrEmpty(_secondaryBtnText))
            {
                var priSize = TextRenderer.MeasureText(g, _primaryBtnText, btnFont);
                var secSize = TextRenderer.MeasureText(g, _secondaryBtnText, btnFont);
                int priW = Math.Max((int)(72 * _scale), priSize.Width + (int)(18 * _scale));
                int secW = Math.Max((int)(60 * _scale), secSize.Width + (int)(18 * _scale));

                int priX = w - padX - priW;
                int secX = priX - (int)(8 * _scale) - secW;

                _secondaryBtnRect = new RectangleF(secX, btnY, secW, btnH);
                Color secBg = _secondaryPressed
                    ? SecondaryBtnPressed
                    : (_secondaryHover ? SecondaryBtnHover : SecondaryBtnBg);
                using (var secPath = Gfx.RoundedRect(_secondaryBtnRect, 6 * _scale))
                {
                    using var secBrush = new SolidBrush(secBg);
                    g.FillPath(secBrush, secPath);
                }
                TextRenderer.DrawText(g, _secondaryBtnText, btnFont,
                    new Rectangle(secX, btnY, secW, btnH),
                    FgColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                _primaryBtnRect = new RectangleF(priX, btnY, priW, btnH);
                Color priBg = _primaryPressed
                    ? AccentBluePressed
                    : (_primaryHover ? AccentBlueHover : AccentBlue);
                using (var priPath = Gfx.RoundedRect(_primaryBtnRect, 6 * _scale))
                {
                    using var priBrush = new SolidBrush(priBg);
                    g.FillPath(priBrush, priPath);
                }
                TextRenderer.DrawText(g, _primaryBtnText, btnFont,
                    new Rectangle(priX, btnY, priW, btnH),
                    Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                var priSize = TextRenderer.MeasureText(g, _primaryBtnText, btnFont);
                int priW = Math.Max((int)(66 * _scale), priSize.Width + (int)(20 * _scale));
                int priX = w - padX - priW;

                _primaryBtnRect = new RectangleF(priX, btnY, priW, btnH);
                Color priBg = _primaryPressed
                    ? AccentBluePressed
                    : (_primaryHover ? AccentBlueHover : AccentBlue);
                using (var priPath = Gfx.RoundedRect(_primaryBtnRect, 6 * _scale))
                {
                    using var priBrush = new SolidBrush(priBg);
                    g.FillPath(priBrush, priPath);
                }
                TextRenderer.DrawText(g, _primaryBtnText, btnFont,
                    new Rectangle(priX, btnY, priW, btnH),
                    Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // 6. 外边框描边
        using var strokePen = new Pen(StrokeColor, 1f);
        using var borderPath = Gfx.RoundedRect(new RectangleF(0.5f, 0.5f, w - 1f, h - 1f), _cornerRadius - 0.5f);
        g.DrawPath(strokePen, borderPath);
    }

    private void DrawStatusIcon(Graphics g, int x, int y, int size)
    {
        float cx = x + size / 2f;
        float cy = y + size / 2f;
        float r = size / 2f;

        switch (_type)
        {
            case ToastType.Success:
                using (var fill = new SolidBrush(SuccessColor))
                {
                    g.FillEllipse(fill, x, y, size, size);
                }
                // 白色勾
                using (var pen = new Pen(Color.White, 2f * _scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx - r * 0.45f, cy + r * 0.05f),
                        new PointF(cx - r * 0.12f, cy + r * 0.38f),
                        new PointF(cx + r * 0.45f, cy - r * 0.35f),
                    });
                }
                break;

            case ToastType.Warning:
                // 圆角三角
                float triH = r * 1.8f;
                float triW = triH * 1.15f;
                PointF[] tri =
                [
                    new(cx, cy - triH / 2f + r * 0.05f),
                    new(cx - triW / 2f, cy + triH / 2f - r * 0.05f),
                    new(cx + triW / 2f, cy + triH / 2f - r * 0.05f),
                ];
                using (var fill = new SolidBrush(WarningColor))
                {
                    g.FillPolygon(fill, tri);
                }
                using (var pen = new Pen(Color.White, 2.2f * _scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, cx, cy - r * 0.28f, cx, cy + r * 0.15f);
                    float dotR = 1.3f * _scale;
                    using var dotBrush = new SolidBrush(Color.White);
                    g.FillEllipse(dotBrush, cx - dotR, cy + r * 0.38f - dotR, dotR * 2, dotR * 2);
                }
                break;

            case ToastType.Error:
                using (var fill = new SolidBrush(ErrorColor))
                {
                    g.FillEllipse(fill, x, y, size, size);
                }
                using (var pen = new Pen(Color.White, 2f * _scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float d = r * 0.35f;
                    g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
                    g.DrawLine(pen, cx + d, cy - d, cx - d, cy + d);
                }
                break;

            case ToastType.UpdateReady:
            case ToastType.Checking:
                using (var fill = new SolidBrush(InfoColor))
                {
                    g.FillEllipse(fill, x, y, size, size);
                }
                // 向下箭头或更新标识
                using (var pen = new Pen(Color.White, 2f * _scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLine(pen, cx, cy - r * 0.45f, cx, cy + r * 0.25f);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx - r * 0.35f, cy - r * 0.05f),
                        new PointF(cx, cy + r * 0.32f),
                        new PointF(cx + r * 0.35f, cy - r * 0.05f),
                    });
                    g.DrawLine(pen, cx - r * 0.4f, cy + r * 0.52f, cx + r * 0.4f, cy + r * 0.52f);
                }
                break;

            default:
                using (var fill = new SolidBrush(InfoColor))
                {
                    g.FillEllipse(fill, x, y, size, size);
                }
                using (var pen = new Pen(Color.White, 2f * _scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float dotR = 1.4f * _scale;
                    using var dotBrush = new SolidBrush(Color.White);
                    g.FillEllipse(dotBrush, cx - dotR, cy - r * 0.45f - dotR, dotR * 2, dotR * 2);
                    g.DrawLine(pen, cx, cy - r * 0.15f, cx, cy + r * 0.45f);
                }
                break;
        }
    }

    // ——— 鼠标交互 ———

    protected override void OnMouseEnter(EventArgs e)
    {
        _autoCloseTimer?.Stop(); // 鼠标悬停时暂停自动关闭
        base.OnMouseEnter(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (_closeBtnRect.Contains(e.Location))
            {
                Close();
                return;
            }
            if (_primaryBtnRect.Contains(e.Location))
            {
                _primaryPressed = true;
                Invalidate();
            }
            else if (_secondaryBtnRect.Contains(e.Location))
            {
                _secondaryPressed = true;
                Invalidate();
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_primaryPressed && _primaryBtnRect.Contains(e.Location))
        {
            var action = _primaryAction;
            Close();
            action?.Invoke();
            return;
        }
        if (_secondaryPressed && _secondaryBtnRect.Contains(e.Location))
        {
            var action = _secondaryAction;
            Close();
            action?.Invoke();
            return;
        }

        _primaryPressed = false;
        _secondaryPressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool ch = _closeBtnRect.Contains(e.Location);
        bool ph = _primaryBtnRect.Contains(e.Location);
        bool sh = _secondaryBtnRect.Contains(e.Location);

        if (ch != _closeHover || ph != _primaryHover || sh != _secondaryHover)
        {
            _closeHover = ch;
            _primaryHover = ph;
            _secondaryHover = sh;
            Cursor = (ch || ph || sh) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_closeHover || _primaryHover || _secondaryHover)
        {
            _closeHover = false;
            _primaryHover = false;
            _secondaryHover = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
        _autoCloseTimer?.Start();
        base.OnMouseLeave(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (s_current == this)
        {
            s_current = null;
        }
        _autoCloseTimer?.Dispose();
        _animTimer?.Dispose();
        base.OnFormClosed(e);
    }

    // ——— 静态便捷 API ———

    /// <summary>显示普通 macOS 26 浮动通知（4 秒自动关闭）。</summary>
    public static void Show(string title, string message, ToastType type = ToastType.Info, int autoCloseMs = 4000)
    {
        try
        {
            s_current?.Close();
            s_current = new MacToast(title, message, type, autoCloseMs: autoCloseMs);
            s_current.Show();
        }
        catch { }
    }

    /// <summary>检查更新：正在连接服务器。</summary>
    public static void ShowChecking()
    {
        Show("检查更新", "正在连接更新服务器…", ToastType.Checking, autoCloseMs: 3000);
    }

    /// <summary>检查更新：已是最新版本。</summary>
    public static void ShowUpToDate(string version)
    {
        Show("已是最新版本", $"当前版本 v{version} 已是最新发布版本。", ToastType.Success, autoCloseMs: 4000);
    }

    /// <summary>检查更新：已就绪新版本，带“重启并更新”与“稍后”操作按钮。</summary>
    public static void ShowUpdateReady(string version, Action onApplyAndRestart)
    {
        try
        {
            s_current?.Close();
            s_current = new MacToast(
                "更新已就绪",
                $"新版本 v{version} 已下载就绪；退出时将自动完成替换，也可立即重启生效。",
                ToastType.UpdateReady,
                primaryBtnText: "重启并更新",
                primaryAction: onApplyAndRestart,
                secondaryBtnText: "稍后",
                secondaryAction: null,
                autoCloseMs: 0); // 需用户主动交互，不自动关闭
            s_current.Show();
        }
        catch { }
    }

    /// <summary>检查更新：失败提示，带“去发布页”与“好”按钮。</summary>
    public static void ShowUpdateFailed(string detail, string pageUrl)
    {
        try
        {
            s_current?.Close();
            s_current = new MacToast(
                "检查更新失败",
                detail,
                ToastType.Warning,
                primaryBtnText: "好",
                primaryAction: null,
                secondaryBtnText: "查看发布页",
                secondaryAction: () =>
                {
                    try { Process.Start(new ProcessStartInfo(pageUrl) { UseShellExecute = true }); } catch { }
                },
                autoCloseMs: 8000);
            s_current.Show();
        }
        catch { }
    }
}
