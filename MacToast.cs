using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Numlock;

/// <summary>
/// macOS 26 (Tahoe) Liquid Glass 右下角浮动通知卡片：
/// · 位于屏幕右下角工作区，不夺取前台输入焦点（ShowWithoutActivation）
/// · 液态玻璃光学渐变面板 + 顶部镜面高光折射边 + DWM 瞬态亚克力材质
/// · 全胶囊交互按钮 + 字体/磁贴缓存 + 安全定时器生命周期（彻底消除关闭竞态异常）
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

    private float _scale;
    private bool _dwmRounded;
    private RectangleF _closeBtnRect;
    private RectangleF _primaryBtnRect;
    private RectangleF _secondaryBtnRect;

    private bool _closeHover;
    private bool _primaryHover;
    private bool _primaryPressed;
    private bool _secondaryHover;
    private bool _secondaryPressed;

    private System.Windows.Forms.Timer? _autoCloseTimer;
    private System.Windows.Forms.Timer? _animTimer;

    private Font? _appNameFont;
    private Font? _catFont;
    private Font? _titleFont;
    private Font? _msgFont;
    private Font? _btnBoldFont;
    private Font? _btnRegFont;

    private LiquidGlass.Palette Pal => LiquidGlass.Get(_dark);

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
        RebuildFonts();

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        Opacity = 0.05;
        BackColor = Pal.WindowSolidFallback;

        int width = (int)Math.Round(370 * _scale);
        int baseHeight = _hasButtons ? (int)Math.Round(152 * _scale) : (int)Math.Round(116 * _scale);
        ClientSize = new Size(width, baseHeight);

        var screen = Screen.FromPoint(Cursor.Position);
        var wa = screen.WorkingArea;
        int margin = (int)Math.Round(16 * _scale);
        Location = new Point(wa.Right - width - margin, wa.Bottom - baseHeight - margin);

        HandleCreated += (_, _) => UpdateWindowRegion();

        _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animTimer.Tick += (_, _) =>
        {
            if (IsDisposed || Disposing)
            {
                LiquidGlass.DisposeTimer(ref _animTimer);
                return;
            }
            if (Opacity < 1.0)
            {
                Opacity = Math.Min(1.0, Opacity + 0.14);
            }
            else
            {
                LiquidGlass.DisposeTimer(ref _animTimer);
            }
        };
        _animTimer.Start();

        if (!_hasButtons && autoCloseMs > 0)
        {
            _autoCloseTimer = new System.Windows.Forms.Timer { Interval = autoCloseMs };
            _autoCloseTimer.Tick += (_, _) =>
            {
                LiquidGlass.DisposeTimer(ref _autoCloseTimer);
                if (!IsDisposed && !Disposing)
                {
                    Close();
                }
            };
            _autoCloseTimer.Start();
        }
    }

    private void RebuildFonts()
    {
        DisposeFonts();
        _appNameFont = LiquidGlass.CreateFont(12f * _scale, FontStyle.Bold);
        _catFont = LiquidGlass.CreateFont(11.5f * _scale, FontStyle.Regular);
        _titleFont = LiquidGlass.CreateFont(14f * _scale, FontStyle.Bold);
        _msgFont = LiquidGlass.CreateFont(12.5f * _scale, FontStyle.Regular);
        _btnBoldFont = LiquidGlass.CreateFont(12.5f * _scale, FontStyle.Bold);
        _btnRegFont = LiquidGlass.CreateFont(12.5f * _scale, FontStyle.Regular);
    }

    private void DisposeFonts()
    {
        _appNameFont?.Dispose();
        _catFont?.Dispose();
        _titleFont?.Dispose();
        _msgFont?.Dispose();
        _btnBoldFont?.Dispose();
        _btnRegFont?.Dispose();
        _appNameFont = _catFont = _titleFont = _msgFont = _btnBoldFont = _btnRegFont = null;
    }

    private void UpdateWindowRegion()
    {
        _dwmRounded = LiquidGlass.ConfigureGlassWindow(this, _dark, 14f * _scale);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _scale = e.DeviceDpiNew / 96f;
        RebuildFonts();
        Bounds = e.SuggestedRectangle;
        UpdateWindowRegion();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        LiquidGlass.ConfigureGraphics(g);
        var p = Pal;

        int w = ClientSize.Width;
        int h = ClientSize.Height;

        LiquidGlass.FillWindowSurface(g, new RectangleF(0, 0, w, h), p);

        // 1. 顶部微型标头
        int padX = (int)Math.Round(14 * _scale);
        int topY = (int)Math.Round(10 * _scale);
        int miniIconSize = (int)Math.Round(16 * _scale);
        LiquidGlass.DrawCachedAppTile(g, new RectangleF(padX, topY, miniIconSize, miniIconSize), miniIconSize * 2);

        var appNameSize = g.MeasureString("Numlock", _appNameFont!);
        LiquidGlass.DrawTextLeft(g, "Numlock", _appNameFont!, p.TextPrimary,
            new RectangleF(padX + miniIconSize + 6 * _scale, topY - 1, appNameSize.Width, miniIconSize + 2));

        string catText = _type is ToastType.Checking or ToastType.UpdateReady ? "检查更新" : "状态提醒";
        int catX = padX + miniIconSize + (int)Math.Round(6 * _scale) + (int)Math.Ceiling(appNameSize.Width) + (int)Math.Round(4 * _scale);
        LiquidGlass.DrawTextLeft(g, "· " + catText, _catFont!, p.TextSecondary,
            new RectangleF(catX, topY - 1, 120 * _scale, miniIconSize + 2));

        // 右上角精致关闭叉
        int closeBtnSize = (int)Math.Round(14 * _scale);
        int closeX = w - padX - closeBtnSize;
        int closeY = topY;
        _closeBtnRect = new RectangleF(closeX - 4, closeY - 4, closeBtnSize + 8, closeBtnSize + 8);
        if (_closeHover)
        {
            using var hoverBg = new SolidBrush(Color.FromArgb(_dark ? 36 : 22, p.TextPrimary));
            g.FillEllipse(hoverBg, closeX - 2, closeY - 2, closeBtnSize + 4, closeBtnSize + 4);
        }
        using (var closePen = new Pen(_closeHover ? p.TextPrimary : p.TextSecondary, 1.3f * _scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        })
        {
            float cx = closeX + closeBtnSize / 2f;
            float cy = closeY + closeBtnSize / 2f;
            float r = closeBtnSize * 0.30f;
            g.DrawLine(closePen, cx - r, cy - r, cx + r, cy + r);
            g.DrawLine(closePen, cx + r, cy - r, cx - r, cy + r);
        }

        // 2. 左侧 Liquid Glass 状态图标
        int bodyY = topY + miniIconSize + (int)Math.Round(10 * _scale);
        int iconSize = (int)Math.Round(32 * _scale);
        DrawStatusIcon(g, padX, bodyY, iconSize);

        // 3. 右侧层次排版文字
        int textX = padX + iconSize + (int)Math.Round(12 * _scale);
        int textW = w - textX - padX;
        LiquidGlass.DrawTextLeft(g, _title, _titleFont!, p.TextPrimary, new RectangleF(textX, bodyY, textW, 20 * _scale));

        int msgY = bodyY + (int)Math.Round(22 * _scale);
        LiquidGlass.DrawTextLeft(g, _message, _msgFont!, p.TextSecondary, new RectangleF(textX, msgY, textW, 46 * _scale), wordBreak: true);

        // 4. 底部全胶囊交互按钮
        if (_hasButtons)
        {
            int btnH = (int)Math.Round(28 * _scale);
            int btnY = h - (int)Math.Round(12 * _scale) - btnH;

            var priSize = g.MeasureString(_primaryBtnText, _btnBoldFont!);
            int priW = Math.Max((int)Math.Round(72 * _scale), (int)Math.Ceiling(priSize.Width) + (int)Math.Round(22 * _scale));
            int priX = w - padX - priW;
            _primaryBtnRect = new RectangleF(priX, btnY, priW, btnH);

            if (!string.IsNullOrEmpty(_secondaryBtnText))
            {
                var secSize = g.MeasureString(_secondaryBtnText, _btnRegFont!);
                int secW = Math.Max((int)Math.Round(62 * _scale), (int)Math.Ceiling(secSize.Width) + (int)Math.Round(22 * _scale));
                int secX = priX - (int)Math.Round(8 * _scale) - secW;
                _secondaryBtnRect = new RectangleF(secX, btnY, secW, btnH);
                LiquidGlass.DrawPillButton(g, _secondaryBtnRect, _secondaryBtnText, _btnRegFont!, p, isPrimary: false, _secondaryHover, _secondaryPressed);
            }

            LiquidGlass.DrawPillButton(g, _primaryBtnRect, _primaryBtnText, _btnBoldFont!, p, isPrimary: true, _primaryHover, _primaryPressed);
        }

        // 5. 外围 Liquid Glass 光学边缘高光
        float borderRadius = _dwmRounded ? 8f * _scale : 14f * _scale;
        var rimRect = new RectangleF(0.5f, 0.5f, w - 1f, h - 1f);
        using var borderPath = Gfx.RoundedRect(rimRect, Math.Max(1f, borderRadius - 0.5f));
        LiquidGlass.DrawGlassRim(g, borderPath, rimRect, p, 1f);
    }

    private void DrawStatusIcon(Graphics g, int x, int y, int size)
    {
        var p = Pal;
        float cx = x + size / 2f;
        float cy = y + size / 2f;
        float r = size / 2f;

        switch (_type)
        {
            case ToastType.Success:
                LiquidGlass.DrawGlassOrb(g, x, y, size, p.SuccessGreen);
                using (var pen = new Pen(Color.White, 2f * _scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLines(pen,
                    [
                        new PointF(cx - r * 0.42f, cy + r * 0.04f),
                        new PointF(cx - r * 0.11f, cy + r * 0.35f),
                        new PointF(cx + r * 0.42f, cy - r * 0.32f),
                    ]);
                }
                break;

            case ToastType.Warning:
                LiquidGlass.DrawGlassOrb(g, x, y, size, p.WarningAmber);
                using (var pen = new Pen(Color.White, 2.2f * _scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var dotBrush = new SolidBrush(Color.White))
                {
                    g.DrawLine(pen, cx, cy - r * 0.34f, cx, cy + r * 0.12f);
                    float dotR = 1.3f * _scale;
                    g.FillEllipse(dotBrush, cx - dotR, cy + r * 0.38f - dotR, dotR * 2f, dotR * 2f);
                }
                break;

            case ToastType.Error:
                LiquidGlass.DrawGlassOrb(g, x, y, size, p.ErrorRed);
                using (var pen = new Pen(Color.White, 2f * _scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float d = r * 0.34f;
                    g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
                    g.DrawLine(pen, cx + d, cy - d, cx - d, cy + d);
                }
                break;

            case ToastType.UpdateReady:
            case ToastType.Checking:
                LiquidGlass.DrawGlassOrb(g, x, y, size, p.AccentSolid);
                using (var pen = new Pen(Color.White, 2f * _scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLine(pen, cx, cy - r * 0.42f, cx, cy + r * 0.22f);
                    g.DrawLines(pen,
                    [
                        new PointF(cx - r * 0.32f, cy - r * 0.04f),
                        new PointF(cx, cy + r * 0.28f),
                        new PointF(cx + r * 0.32f, cy - r * 0.04f),
                    ]);
                    g.DrawLine(pen, cx - r * 0.38f, cy + r * 0.48f, cx + r * 0.38f, cy + r * 0.48f);
                }
                break;

            default:
                LiquidGlass.DrawGlassOrb(g, x, y, size, p.AccentSolid);
                using (var pen = new Pen(Color.White, 2f * _scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var dotBrush = new SolidBrush(Color.White))
                {
                    float dotR = 1.4f * _scale;
                    g.FillEllipse(dotBrush, cx - dotR, cy - r * 0.44f - dotR, dotR * 2f, dotR * 2f);
                    g.DrawLine(pen, cx, cy - r * 0.14f, cx, cy + r * 0.44f);
                }
                break;
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        if (!IsDisposed && !Disposing)
        {
            _autoCloseTimer?.Stop();
        }
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
        if (!IsDisposed && !Disposing)
        {
            _autoCloseTimer?.Start();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (s_current == this)
        {
            s_current = null;
        }
        LiquidGlass.DisposeTimer(ref _autoCloseTimer);
        LiquidGlass.DisposeTimer(ref _animTimer);
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (s_current == this)
            {
                s_current = null;
            }
            LiquidGlass.DisposeTimer(ref _autoCloseTimer);
            LiquidGlass.DisposeTimer(ref _animTimer);
            DisposeFonts();
        }
        base.Dispose(disposing);
    }

    public static void Show(string title, string message, ToastType type = ToastType.Info, int autoCloseMs = 4000)
    {
        try
        {
            s_current?.Close();
            s_current = new MacToast(title, message, type, autoCloseMs: autoCloseMs);
            s_current.Show();
        }
        catch
        {
        }
    }

    public static void ShowChecking() =>
        Show("检查更新", "正在连接更新服务器…", ToastType.Checking, autoCloseMs: 3000);

    public static void ShowUpToDate(string version) =>
        Show("已是最新版本", $"当前版本 v{version} 已是最新发布版本。", ToastType.Success, autoCloseMs: 4000);

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
                autoCloseMs: 0);
            s_current.Show();
        }
        catch
        {
        }
    }

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
        catch
        {
        }
    }

    public static void CloseIfOpen()
    {
        try
        {
            if (s_current != null && !s_current.IsDisposed)
            {
                s_current.Close();
            }
        }
        catch
        {
        }
    }
}
