using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Numlock;

/// <summary>
/// macOS 26 (Tahoe) Liquid Glass 独立“软件更新”模态卡片窗口：
/// · 液态玻璃光学渐变面板 + 顶部镜面高光折射边 + DWM 瞬态亚克力材质
/// · 同心连续曲率圆角（Concentric Squircle）+ 经典交通灯红点关闭控制 + 全窗无闪烁拖拽
/// · 52px 立体浮雕键帽磁贴（复用缓存，Spinner 25 FPS 旋转期间零位图/字体分配）
/// · Liquid Glass 内嵌状态卡片（检查中 / 发现更新 / 实时进度下载 / 已是最新 / 就绪待重启 / 检查失败 / 升级成功）
/// · 全胶囊交互按钮（Full Pill Capsule）+ 沉浸式深/浅双模 + Esc/Enter 快捷键
/// </summary>
internal sealed class MacUpdateWindow : Form
{
    internal enum UpdateState
    {
        Checking,
        Available,
        Downloading,
        UpToDate,
        UpdateReady,
        Failed,
        Success
    }

    private static MacUpdateWindow? s_instance;

    private readonly bool _dark;
    private float _scale;
    private bool _dwmRounded;

    private UpdateState _state;
    private string _currentVersion;
    private string _targetVersion = "";
    private string _detailMessage = "";
    private string _pageUrl = Updater.PageUrl;
    private DateTime _checkTime = DateTime.Now;

    private Action? _onRestart;
    private Action? _onRetry;
    private Action? _onDownload;
    private Action? _onCancelDownload;

    // 下载进度与目标体积跟踪
    private long _targetSizeBytes;
    private long _downloadedBytes;
    private long _totalDownloadBytes;
    private float _downloadProgress = -1f; // 0.0f .. 1.0f，-1 表示未知/初始化中

    // 控件区域交互缓存
    private RectangleF _closeBtnRect;
    private RectangleF _primaryBtnRect;
    private RectangleF _secondaryBtnRect;
    private RectangleF _linkRect;

    private bool _closeHover;
    private bool _closePressed;
    private bool _primaryHover;
    private bool _primaryPressed;
    private bool _secondaryHover;
    private bool _secondaryPressed;
    private bool _linkHover;

    // 动画计时器（可空并在销毁时置空，消除 ObjectDisposedException）
    private System.Windows.Forms.Timer? _animTimer;
    private System.Windows.Forms.Timer? _spinnerTimer;
    private int _spinnerStep;

    // 按 DPI 缓存字体，确保 25 FPS 动画期间零 GDI+ 字体分配
    private Font? _titleFont;
    private Font? _nameFont;
    private Font? _verFont;
    private Font? _headFont;
    private Font? _headSmallFont;
    private Font? _descFont;
    private Font? _descSmallFont;
    private Font? _hintFont;
    private Font? _linkFont;
    private Font? _linkHoverFont;
    private Font? _btnBoldFont;
    private Font? _btnRegFont;

    private LiquidGlass.Palette Pal => LiquidGlass.Get(_dark);

    public static bool IsVisible => s_instance != null && !s_instance.IsDisposed && s_instance.Visible;

    private MacUpdateWindow(string currentVersion, UpdateState initialState, bool? dark = null, bool animateFade = true)
    {
        _currentVersion = currentVersion;
        _state = initialState;
        _dark = dark ?? !SystemTheme.UsesLightTaskbar();
        _scale = DeviceDpi / 96f;
        RebuildFonts();

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        Opacity = animateFade ? 0.05 : 1.0;
        BackColor = Pal.WindowSolidFallback;
        KeyPreview = true;

        int width = (int)Math.Round(390 * _scale);
        int height = (int)Math.Round(384 * _scale);
        ClientSize = new Size(width, height);

        var screen = Screen.FromPoint(Cursor.Position);
        var wa = screen.WorkingArea;
        Location = new Point(
            wa.Left + Math.Max(0, (wa.Width - width) / 2),
            wa.Top + Math.Max(0, (wa.Height - height) / 2));

        HandleCreated += (_, _) => UpdateWindowRegion();

        if (animateFade)
        {
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

            _spinnerTimer = new System.Windows.Forms.Timer { Interval = 45 };
            _spinnerTimer.Tick += (_, _) =>
            {
                if (IsDisposed || Disposing)
                {
                    LiquidGlass.DisposeTimer(ref _spinnerTimer);
                    return;
                }
                if (_state == UpdateState.Checking || _state == UpdateState.Downloading)
                {
                    _spinnerStep = (_spinnerStep + 1) % 12;
                    Invalidate();
                }
                else
                {
                    _spinnerTimer?.Stop();
                }
            };

            if (_state == UpdateState.Checking || _state == UpdateState.Downloading)
            {
                _spinnerTimer.Start();
            }
        }
    }

    private void RebuildFonts()
    {
        DisposeFonts();
        _titleFont = LiquidGlass.CreateFont(12.5f * _scale, FontStyle.Regular);
        _nameFont = LiquidGlass.CreateFont(20f * _scale, FontStyle.Bold);
        _verFont = LiquidGlass.CreateFont(11.5f * _scale, FontStyle.Bold);
        _headFont = LiquidGlass.CreateFont(15.5f * _scale, FontStyle.Bold);
        _headSmallFont = LiquidGlass.CreateFont(15f * _scale, FontStyle.Bold);
        _descFont = LiquidGlass.CreateFont(12.5f * _scale, FontStyle.Regular);
        _descSmallFont = LiquidGlass.CreateFont(11.5f * _scale, FontStyle.Regular);
        _hintFont = LiquidGlass.CreateFont(10.5f * _scale, FontStyle.Regular);
        _linkFont = LiquidGlass.CreateFont(11.5f * _scale, FontStyle.Regular);
        _linkHoverFont = LiquidGlass.CreateFont(11.5f * _scale, FontStyle.Underline);
        _btnBoldFont = LiquidGlass.CreateFont(13.5f * _scale, FontStyle.Bold);
        _btnRegFont = LiquidGlass.CreateFont(13.5f * _scale, FontStyle.Regular);
    }

    private void DisposeFonts()
    {
        _titleFont?.Dispose();
        _nameFont?.Dispose();
        _verFont?.Dispose();
        _headFont?.Dispose();
        _headSmallFont?.Dispose();
        _descFont?.Dispose();
        _descSmallFont?.Dispose();
        _hintFont?.Dispose();
        _linkFont?.Dispose();
        _linkHoverFont?.Dispose();
        _btnBoldFont?.Dispose();
        _btnRegFont?.Dispose();
        _titleFont = _nameFont = _verFont = _headFont = _headSmallFont = _descFont =
            _descSmallFont = _hintFont = _linkFont = _linkHoverFont = _btnBoldFont = _btnRegFont = null;
    }

    private void UpdateWindowRegion()
    {
        _dwmRounded = LiquidGlass.ConfigureGlassWindow(this, _dark, 16f * _scale);
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Enter)
        {
            switch (_state)
            {
                case UpdateState.Available:
                    HandlePrimaryAction();
                    break;
                case UpdateState.UpToDate:
                case UpdateState.Success:
                    Close();
                    break;
                case UpdateState.UpdateReady:
                    var restartAction = _onRestart;
                    Close();
                    restartAction?.Invoke();
                    break;
                case UpdateState.Failed:
                    TriggerRetry();
                    break;
                case UpdateState.Checking:
                case UpdateState.Downloading:
                    Close();
                    break;
            }
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintSurface(e.Graphics, ClientSize.Width, ClientSize.Height, clipToSquircle: false);
    }

    private void PaintSurface(Graphics g, int w, int h, bool clipToSquircle)
    {
        LiquidGlass.ConfigureGraphics(g);
        var p = Pal;

        float borderRadius = clipToSquircle || !_dwmRounded ? 16f * _scale : 8f * _scale;
        var outerBounds = new RectangleF(0, 0, w, h);

        using var clipPath = clipToSquircle ? Gfx.RoundedRect(outerBounds, borderRadius) : null;
        LiquidGlass.FillWindowSurface(g, outerBounds, p, clipPath);

        // 1. 顶部标题栏（交通灯红点 + 居中标题）
        float dotSize = 12f * _scale;
        float dotX = 16f * _scale;
        float dotY = 14f * _scale;
        _closeBtnRect = new RectangleF(dotX - 3, dotY - 3, dotSize + 6, dotSize + 6);
        LiquidGlass.DrawTrafficClose(g, dotX, dotY, dotSize, _scale, _closeHover, _closePressed);

        LiquidGlass.DrawTextCenter(g, "软件更新", _titleFont!, p.TextSecondary, new RectangleF(0, 12 * _scale, w, 20 * _scale));

        // 2. 应用标识（复用缓存磁贴 + 品牌名 + 液态玻璃版本徽标）
        int iconSize = (int)Math.Round(52 * _scale);
        int iconX = (w - iconSize) / 2;
        int iconY = (int)Math.Round(42 * _scale);
        LiquidGlass.DrawCachedAppTile(g, new RectangleF(iconX, iconY, iconSize, iconSize), iconSize * 2);

        var nameSize = g.MeasureString("Numlock", _nameFont!);
        int nameY = iconY + iconSize + (int)Math.Round(6 * _scale);
        LiquidGlass.DrawTextCenter(g, "Numlock", _nameFont!, p.TextPrimary, new RectangleF(0, nameY, w, nameSize.Height));

        string verBadge = "当前版本 v" + _currentVersion;
        var verSize = LiquidGlass.MeasureTightText(g, verBadge, _verFont!);
        int badgeW = (int)Math.Ceiling(verSize.Width) + (int)Math.Round(22 * _scale);
        int badgeH = (int)Math.Ceiling(verSize.Height) + (int)Math.Round(6 * _scale);
        int badgeX = (w - badgeW) / 2;
        int badgeY = nameY + (int)Math.Ceiling(nameSize.Height) + (int)Math.Round(4 * _scale);
        LiquidGlass.DrawVersionBadge(g, new RectangleF(badgeX, badgeY, badgeW, badgeH), verBadge, _verFont!, p);

        // 3. Liquid Glass 内嵌状态卡片（同心圆角 12px）
        int cardMargin = (int)Math.Round(20 * _scale);
        int cardX = cardMargin;
        int cardY = badgeY + badgeH + (int)Math.Round(14 * _scale);
        int cardW = w - cardMargin * 2;
        int cardH = (int)Math.Round(142 * _scale);
        LiquidGlass.DrawInsetCard(g, new RectangleF(cardX, cardY, cardW, cardH), 12f * _scale, p);

        _linkRect = RectangleF.Empty;
        DrawCardContent(g, cardX, cardY, cardW, cardH);

        // 4. 底部全胶囊操作按钮
        DrawActionButtons(g, w, h);

        // 5. 外围 Liquid Glass 光学边缘高光
        var rimRect = new RectangleF(0.5f, 0.5f, w - 1f, h - 1f);
        using var borderPath = Gfx.RoundedRect(rimRect, Math.Max(1f, borderRadius - 0.5f));
        LiquidGlass.DrawGlassRim(g, borderPath, rimRect, p, 1f);
    }

    private void DrawCardContent(Graphics g, int x, int y, int w, int h)
    {
        switch (_state)
        {
            case UpdateState.Checking:
                DrawCheckingCard(g, x, y, w);
                break;
            case UpdateState.Available:
                DrawAvailableCard(g, x, y, w);
                break;
            case UpdateState.Downloading:
                DrawDownloadingCard(g, x, y, w);
                break;
            case UpdateState.UpToDate:
                DrawUpToDateCard(g, x, y, w);
                break;
            case UpdateState.UpdateReady:
                DrawUpdateReadyCard(g, x, y, w);
                break;
            case UpdateState.Failed:
                DrawFailedCard(g, x, y, w);
                break;
            case UpdateState.Success:
                DrawSuccessCard(g, x, y, w);
                break;
        }
    }

    private void DrawCheckingCard(Graphics g, int x, int y, int w)
    {
        var p = Pal;
        float cx = x + w / 2f;
        float cy = y + 36f * _scale;

        LiquidGlass.DrawMacSpinner(g, cx, cy, 14f * _scale, _scale, p.TextPrimary, _spinnerStep);

        int textY = (int)Math.Round(cy + 24 * _scale);
        LiquidGlass.DrawTextCenter(g, "正在检查更新…", _headFont!, p.TextPrimary, new RectangleF(x, textY, w, 24 * _scale));
        LiquidGlass.DrawTextCenter(g, "正在连接 GitHub 服务器查询最新发布版本…", _descFont!, p.TextSecondary, new RectangleF(x, textY + 24 * _scale, w, 22 * _scale));
    }

    private void DrawAvailableCard(Graphics g, int x, int y, int w)
    {
        var p = Pal;
        float cx = x + w / 2f;
        int iconSize = (int)Math.Round(34 * _scale);
        int iconY = y + (int)Math.Round(13 * _scale);
        int iconX = (int)Math.Round(cx - iconSize / 2f);

        DrawDownloadOrb(g, iconX, iconY, iconSize, p.AccentSolid);

        int textY = iconY + iconSize + (int)Math.Round(8 * _scale);
        LiquidGlass.DrawTextCenter(g, $"发现新版本 Numlock v{_targetVersion}", _headFont!, p.TextPrimary, new RectangleF(x, textY, w, 22 * _scale));

        string sizeStr = _targetSizeBytes > 0 ? $"，新包约 {FormatSize(_targetSizeBytes)}" : "";
        LiquidGlass.DrawTextCenter(
            g,
            $"发现新版本可用（当前运行的是 v{_currentVersion}{sizeStr}）。点击下方按钮开始下载。",
            _descSmallFont!,
            p.TextSecondary,
            new RectangleF(x + 12 * _scale, textY + 22 * _scale, w - 24 * _scale, 32 * _scale),
            wordBreak: true);

        DrawReleaseLink(g, cx, textY + (int)Math.Round(55 * _scale), "查看 GitHub Release 发布说明 →");
    }

    private void DrawDownloadingCard(Graphics g, int x, int y, int w)
    {
        var p = Pal;
        float cx = x + w / 2f;
        float cy = y + 24f * _scale;

        LiquidGlass.DrawMacSpinner(g, cx, cy, 11f * _scale, _scale, p.TextPrimary, _spinnerStep);

        int titleY = (int)Math.Round(cy + 18 * _scale);
        LiquidGlass.DrawTextCenter(g, "正在下载更新…", _headSmallFont!, p.TextPrimary, new RectangleF(x, titleY, w, 22 * _scale));

        // macOS 26 细长胶囊玻璃进度条
        int barH = Math.Max(5, (int)Math.Round(6 * _scale));
        int barMarginX = (int)Math.Round(28 * _scale);
        int barW = w - barMarginX * 2;
        int barX = x + barMarginX;
        int barY = titleY + (int)Math.Round(28 * _scale);

        var trackRect = new RectangleF(barX, barY, barW, barH);
        float barRadius = barH / 2f;

        using (var trackPath = Gfx.RoundedRect(trackRect, barRadius))
        {
            using var trackBrush = new SolidBrush(Color.FromArgb(_dark ? 48 : 26, p.TextPrimary));
            g.FillPath(trackBrush, trackPath);
        }

        if (_downloadProgress > 0f)
        {
            float fillW = Math.Clamp(barW * _downloadProgress, barH, barW);
            var fillRect = new RectangleF(barX, barY, fillW, barH);
            using var fillPath = Gfx.RoundedRect(fillRect, barRadius);
            using var fillBrush = new LinearGradientBrush(
                new PointF(fillRect.X, fillRect.Y),
                new PointF(fillRect.Right, fillRect.Y),
                p.AccentSolid,
                p.AccentHoverTop);
            g.FillPath(fillBrush, fillPath);
        }

        string progressText;
        if (_totalDownloadBytes > 0)
        {
            int pct = (int)Math.Clamp(Math.Round(_downloadProgress * 100), 0, 100);
            progressText = $"已下载 {FormatSize(_downloadedBytes)} / {FormatSize(_totalDownloadBytes)} ({pct}%)";
        }
        else if (_downloadedBytes > 0)
        {
            progressText = $"已下载 {FormatSize(_downloadedBytes)}…";
        }
        else
        {
            string sizeHint = _targetSizeBytes > 0 ? $"（约 {FormatSize(_targetSizeBytes)}）" : "";
            progressText = $"正在从 GitHub 下载极简更新包{sizeHint}，请稍候…";
        }

        int textY = barY + barH + (int)Math.Round(8 * _scale);
        LiquidGlass.DrawTextCenter(g, progressText, _descSmallFont!, p.TextSecondary, new RectangleF(x, textY, w, 20 * _scale));
        LiquidGlass.DrawTextCenter(g, "下载完成将自动校验 SHA-256 并准备就绪", _hintFont!, p.TextMuted, new RectangleF(x, textY + 20 * _scale, w, 18 * _scale));
    }

    internal static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 KB";
        }
        if (bytes < 1024 * 1024)
        {
            return $"{Math.Max(1, (int)Math.Round(bytes / 1024.0))} KB";
        }
        return $"{(bytes / (1024.0 * 1024.0)):0.0} MB";
    }

    private void DrawUpToDateCard(Graphics g, int x, int y, int w)
    {
        var p = Pal;
        float cx = x + w / 2f;
        int iconSize = (int)Math.Round(34 * _scale);
        int iconY = y + (int)Math.Round(15 * _scale);
        int iconX = (int)Math.Round(cx - iconSize / 2f);

        DrawCheckOrb(g, iconX, iconY, iconSize, p.SuccessGreen);

        int textY = iconY + iconSize + (int)Math.Round(10 * _scale);
        LiquidGlass.DrawTextCenter(g, "您使用的是最新版本", _headFont!, p.TextPrimary, new RectangleF(x, textY, w, 22 * _scale));
        LiquidGlass.DrawTextCenter(g, $"Numlock {_currentVersion} 是目前最新的可用版本。", _descFont!, p.TextSecondary, new RectangleF(x, textY + 24 * _scale, w, 20 * _scale));
        LiquidGlass.DrawTextCenter(g, $"最后检查：今天 {_checkTime:HH:mm:ss}", _descSmallFont!, p.TextMuted, new RectangleF(x, textY + 45 * _scale, w, 18 * _scale));
    }

    private void DrawUpdateReadyCard(Graphics g, int x, int y, int w)
    {
        var p = Pal;
        float cx = x + w / 2f;
        int iconSize = (int)Math.Round(34 * _scale);
        int iconY = y + (int)Math.Round(13 * _scale);
        int iconX = (int)Math.Round(cx - iconSize / 2f);

        DrawDownloadOrb(g, iconX, iconY, iconSize, p.AccentSolid);

        int textY = iconY + iconSize + (int)Math.Round(8 * _scale);
        LiquidGlass.DrawTextCenter(g, $"发现新版本 Numlock v{_targetVersion}", _headFont!, p.TextPrimary, new RectangleF(x, textY, w, 22 * _scale));
        LiquidGlass.DrawTextCenter(
            g,
            "新版本已下载就绪；退出时将自动替换，也可立即重启生效。",
            _descSmallFont!,
            p.TextSecondary,
            new RectangleF(x + 12 * _scale, textY + 22 * _scale, w - 24 * _scale, 32 * _scale),
            wordBreak: true);

        DrawReleaseLink(g, cx, textY + (int)Math.Round(55 * _scale), "查看 GitHub Release 发布说明 →");
    }

    private void DrawFailedCard(Graphics g, int x, int y, int w)
    {
        var p = Pal;
        float cx = x + w / 2f;
        int iconSize = (int)Math.Round(34 * _scale);
        int iconY = y + (int)Math.Round(13 * _scale);
        int iconX = (int)Math.Round(cx - iconSize / 2f);

        LiquidGlass.DrawGlassOrb(g, iconX, iconY, iconSize, p.WarningAmber);
        using (var pen = new Pen(Color.White, 2.3f * _scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            float r = iconSize / 2f;
            float icx = iconX + r;
            float icy = iconY + r;
            g.DrawLine(pen, icx, icy - r * 0.35f, icx, icy + r * 0.12f);
            float dotR = 1.35f * _scale;
            using var dotBrush = new SolidBrush(Color.White);
            g.FillEllipse(dotBrush, icx - dotR, icy + r * 0.38f - dotR, dotR * 2f, dotR * 2f);
        }

        int textY = iconY + iconSize + (int)Math.Round(8 * _scale);
        LiquidGlass.DrawTextCenter(g, "无法检查更新", _headFont!, p.TextPrimary, new RectangleF(x, textY, w, 22 * _scale));

        string msg = string.IsNullOrWhiteSpace(_detailMessage) ? "未能连接到更新服务器，请检查网络连接后重试。" : _detailMessage;
        LiquidGlass.DrawTextCenter(g, msg, _descSmallFont!, p.TextSecondary, new RectangleF(x + 14 * _scale, textY + 22 * _scale, w - 28 * _scale, 32 * _scale), wordBreak: true);

        DrawReleaseLink(g, cx, textY + (int)Math.Round(55 * _scale), "前往 GitHub 发布页手动下载 →");
    }

    private void DrawSuccessCard(Graphics g, int x, int y, int w)
    {
        var p = Pal;
        float cx = x + w / 2f;
        int iconSize = (int)Math.Round(34 * _scale);
        int iconY = y + (int)Math.Round(16 * _scale);
        int iconX = (int)Math.Round(cx - iconSize / 2f);

        DrawCheckOrb(g, iconX, iconY, iconSize, p.SuccessGreen);

        int textY = iconY + iconSize + (int)Math.Round(10 * _scale);
        LiquidGlass.DrawTextCenter(g, "更新已成功完成", _headFont!, p.TextPrimary, new RectangleF(x, textY, w, 22 * _scale));
        LiquidGlass.DrawTextCenter(g, $"已成功升级到 v{_targetVersion}，您目前运行的是最新版本。", _descFont!, p.TextSecondary, new RectangleF(x, textY + 24 * _scale, w, 20 * _scale));
    }

    private void DrawCheckOrb(Graphics g, int x, int y, int size, Color color)
    {
        LiquidGlass.DrawGlassOrb(g, x, y, size, color);
        using var pen = new Pen(Color.White, 2.2f * _scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        float r = size / 2f;
        float icx = x + r;
        float icy = y + r;
        g.DrawLines(pen,
        [
            new PointF(icx - r * 0.42f, icy + r * 0.04f),
            new PointF(icx - r * 0.11f, icy + r * 0.35f),
            new PointF(icx + r * 0.42f, icy - r * 0.32f),
        ]);
    }

    private void DrawDownloadOrb(Graphics g, int x, int y, int size, Color color)
    {
        LiquidGlass.DrawGlassOrb(g, x, y, size, color);
        using var pen = new Pen(Color.White, 2.2f * _scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        float r = size / 2f;
        float icx = x + r;
        float icy = y + r;
        g.DrawLine(pen, icx, icy - r * 0.42f, icx, icy + r * 0.22f);
        g.DrawLines(pen,
        [
            new PointF(icx - r * 0.30f, icy - r * 0.04f),
            new PointF(icx, icy + r * 0.26f),
            new PointF(icx + r * 0.30f, icy - r * 0.04f),
        ]);
        g.DrawLine(pen, icx - r * 0.38f, icy + r * 0.48f, icx + r * 0.38f, icy + r * 0.48f);
    }

    private void DrawReleaseLink(Graphics g, float cx, int linkY, string linkText)
    {
        var p = Pal;
        Font activeFont = _linkHover ? _linkHoverFont! : _linkFont!;
        var linkSize = g.MeasureString(linkText, activeFont);
        int linkX = (int)Math.Round(cx - linkSize.Width / 2f);
        _linkRect = new RectangleF(linkX, linkY, linkSize.Width, linkSize.Height);
        LiquidGlass.DrawTextCenter(g, linkText, activeFont, _linkHover ? p.AccentHoverTop : p.AccentSolid, _linkRect);
    }

    private void DrawActionButtons(Graphics g, int w, int h)
    {
        var p = Pal;
        int btnH = (int)Math.Round(32 * _scale);
        int btnY = h - (int)Math.Round(18 * _scale) - btnH;
        int padX = (int)Math.Round(20 * _scale);

        _primaryBtnRect = RectangleF.Empty;
        _secondaryBtnRect = RectangleF.Empty;

        if (_state == UpdateState.Checking || _state == UpdateState.Downloading)
        {
            int btnW = (int)Math.Round(96 * _scale);
            _secondaryBtnRect = new RectangleF((w - btnW) / 2f, btnY, btnW, btnH);
            LiquidGlass.DrawPillButton(g, _secondaryBtnRect, "取消", _btnRegFont!, p, isPrimary: false, _secondaryHover, _secondaryPressed);
        }
        else if (_state == UpdateState.Available)
        {
            DrawDualButtons(g, w, padX, btnY, btnH, "稍后", 78, "立即下载更新", 132);
        }
        else if (_state == UpdateState.UpToDate || _state == UpdateState.Success)
        {
            int btnW = (int)Math.Round(112 * _scale);
            _primaryBtnRect = new RectangleF((w - btnW) / 2f, btnY, btnW, btnH);
            LiquidGlass.DrawPillButton(g, _primaryBtnRect, "好", _btnBoldFont!, p, isPrimary: true, _primaryHover, _primaryPressed);
        }
        else if (_state == UpdateState.UpdateReady)
        {
            DrawDualButtons(g, w, padX, btnY, btnH, "稍后", 78, "重启并更新", 122);
        }
        else if (_state == UpdateState.Failed)
        {
            DrawDualButtons(g, w, padX, btnY, btnH, "好", 78, "重试", 92);
        }
    }

    private void DrawDualButtons(Graphics g, int w, int padX, int btnY, int btnH, string secText, int secLogicalW, string priText, int priLogicalW)
    {
        var p = Pal;
        int priW = (int)Math.Round(priLogicalW * _scale);
        int secW = (int)Math.Round(secLogicalW * _scale);
        int gap = (int)Math.Round(10 * _scale);
        int priX = w - padX - priW;
        int secX = priX - gap - secW;

        _secondaryBtnRect = new RectangleF(secX, btnY, secW, btnH);
        _primaryBtnRect = new RectangleF(priX, btnY, priW, btnH);

        LiquidGlass.DrawPillButton(g, _secondaryBtnRect, secText, _btnRegFont!, p, isPrimary: false, _secondaryHover, _secondaryPressed);
        LiquidGlass.DrawPillButton(g, _primaryBtnRect, priText, _btnBoldFont!, p, isPrimary: true, _primaryHover, _primaryPressed);
    }

    // ——— 鼠标交互与窗口拖拽 ———

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
            if (_primaryBtnRect.Contains(e.Location))
            {
                _primaryPressed = true;
                Invalidate();
                return;
            }
            if (_secondaryBtnRect.Contains(e.Location))
            {
                _secondaryPressed = true;
                Invalidate();
                return;
            }
            if (_linkRect.Contains(e.Location))
            {
                return;
            }

            LiquidGlass.BeginSmoothDrag(Handle);
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

        if (_primaryPressed && _primaryBtnRect.Contains(e.Location))
        {
            _primaryPressed = false;
            Invalidate();
            HandlePrimaryAction();
            return;
        }

        if (_secondaryPressed && _secondaryBtnRect.Contains(e.Location))
        {
            _secondaryPressed = false;
            Invalidate();
            HandleSecondaryAction();
            return;
        }

        if (_linkRect.Contains(e.Location))
        {
            try
            {
                Process.Start(new ProcessStartInfo(_pageUrl) { UseShellExecute = true });
            }
            catch
            {
            }
            return;
        }

        _closePressed = false;
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
        bool lh = _linkRect.Contains(e.Location);

        if (ch != _closeHover || ph != _primaryHover || sh != _secondaryHover || lh != _linkHover)
        {
            _closeHover = ch;
            _primaryHover = ph;
            _secondaryHover = sh;
            _linkHover = lh;
            Cursor = (ch || ph || sh || lh) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_closeHover || _primaryHover || _secondaryHover || _linkHover)
        {
            _closeHover = false;
            _primaryHover = false;
            _secondaryHover = false;
            _linkHover = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    private void HandlePrimaryAction()
    {
        switch (_state)
        {
            case UpdateState.Available:
                _onDownload?.Invoke();
                break;
            case UpdateState.UpToDate:
            case UpdateState.Success:
                Close();
                break;
            case UpdateState.UpdateReady:
                var restartAction = _onRestart;
                Close();
                restartAction?.Invoke();
                break;
            case UpdateState.Failed:
                TriggerRetry();
                break;
        }
    }

    private void HandleSecondaryAction()
    {
        switch (_state)
        {
            case UpdateState.Checking:
            case UpdateState.Available:
            case UpdateState.UpdateReady:
            case UpdateState.Failed:
                Close();
                break;
            case UpdateState.Downloading:
                CancelDownloadOnce();
                Close();
                break;
        }
    }

    private void CancelDownloadOnce()
    {
        var cancel = _onCancelDownload;
        _onCancelDownload = null;
        cancel?.Invoke();
    }

    private void TriggerRetry()
    {
        _state = UpdateState.Checking;
        _spinnerStep = 0;
        _spinnerTimer?.Start();
        Invalidate();
        _onRetry?.Invoke();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_state == UpdateState.Downloading)
        {
            CancelDownloadOnce();
        }
        if (s_instance == this)
        {
            s_instance = null;
        }
        LiquidGlass.DisposeTimer(ref _spinnerTimer);
        LiquidGlass.DisposeTimer(ref _animTimer);
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_state == UpdateState.Downloading)
            {
                CancelDownloadOnce();
            }
            if (s_instance == this)
            {
                s_instance = null;
            }
            LiquidGlass.DisposeTimer(ref _spinnerTimer);
            LiquidGlass.DisposeTimer(ref _animTimer);
            DisposeFonts();
        }
        base.Dispose(disposing);
    }

    // ——— 静态公共调用入口 ———

    public static void ShowChecking(Action? onRetry = null, string pageUrl = Updater.PageUrl)
    {
        EnsureInstance(UpdateState.Checking);
        if (s_instance != null)
        {
            s_instance._onRetry = onRetry;
            s_instance._pageUrl = pageUrl;
            s_instance._state = UpdateState.Checking;
            s_instance._spinnerStep = 0;
            s_instance._spinnerTimer?.Start();
            s_instance.RefreshAndFocus();
        }
    }

    public static void SetResultAvailable(string version, Action onDownload, string pageUrl = Updater.PageUrl, long targetSizeBytes = 0)
        => ShowAvailable(version, onDownload, pageUrl, targetSizeBytes);

    public static void ShowAvailable(string version, Action onDownload, string pageUrl = Updater.PageUrl, long targetSizeBytes = 0)
    {
        EnsureInstance(UpdateState.Available);
        if (s_instance != null)
        {
            s_instance._targetVersion = version;
            s_instance._onDownload = onDownload;
            s_instance._pageUrl = pageUrl;
            s_instance._targetSizeBytes = targetSizeBytes;
            s_instance._state = UpdateState.Available;
            s_instance._spinnerTimer?.Stop();
            s_instance.RefreshAndFocus();
        }
    }

    public static void ShowDownloading(Action onCancel, long targetSizeBytes = 0)
    {
        EnsureInstance(UpdateState.Downloading);
        if (s_instance != null)
        {
            s_instance._onCancelDownload = onCancel;
            if (targetSizeBytes > 0)
            {
                s_instance._targetSizeBytes = targetSizeBytes;
            }
            s_instance._downloadedBytes = 0;
            s_instance._totalDownloadBytes = 0;
            s_instance._downloadProgress = -1f;
            s_instance._state = UpdateState.Downloading;
            s_instance._spinnerStep = 0;
            s_instance._spinnerTimer?.Start();
            s_instance.RefreshAndFocus();
        }
    }

    public static void UpdateProgress(long downloaded, long total)
    {
        var inst = s_instance;
        if (inst == null || inst.IsDisposed)
        {
            return;
        }
        if (inst.InvokeRequired)
        {
            if (!inst.IsHandleCreated)
            {
                return;
            }
            try
            {
                inst.BeginInvoke(new Action(() => UpdateProgress(downloaded, total)));
            }
            catch
            {
            }
            return;
        }

        if (inst._state == UpdateState.Downloading)
        {
            inst._downloadedBytes = downloaded;
            inst._totalDownloadBytes = total;
            inst._downloadProgress = total > 0 ? Math.Clamp((float)downloaded / total, 0f, 1f) : -1f;
            inst.Invalidate();
        }
    }

    public static void SetResultUpToDate(string version) => ShowUpToDate(version);

    public static void ShowUpToDate(string version)
    {
        EnsureInstance(UpdateState.UpToDate);
        if (s_instance != null)
        {
            s_instance._currentVersion = version;
            s_instance._checkTime = DateTime.Now;
            s_instance._state = UpdateState.UpToDate;
            s_instance._spinnerTimer?.Stop();
            s_instance.RefreshAndFocus();
        }
    }

    public static void SetResultReady(string version, Action onRestart, string pageUrl = Updater.PageUrl)
        => ShowReady(version, onRestart, pageUrl);

    public static void ShowReady(string version, Action onRestart, string pageUrl = Updater.PageUrl)
    {
        EnsureInstance(UpdateState.UpdateReady);
        if (s_instance != null)
        {
            s_instance._targetVersion = version;
            s_instance._onRestart = onRestart;
            s_instance._pageUrl = pageUrl;
            s_instance._state = UpdateState.UpdateReady;
            s_instance._spinnerTimer?.Stop();
            s_instance.RefreshAndFocus();
        }
    }

    public static void SetResultFailed(string detail, Action? onRetry, string pageUrl = Updater.PageUrl)
        => ShowFailed(detail, onRetry, pageUrl);

    public static void ShowFailed(string detail, Action? onRetry, string pageUrl = Updater.PageUrl)
    {
        EnsureInstance(UpdateState.Failed);
        if (s_instance != null)
        {
            s_instance._detailMessage = detail;
            s_instance._onRetry = onRetry;
            s_instance._pageUrl = pageUrl;
            s_instance._state = UpdateState.Failed;
            s_instance._spinnerTimer?.Stop();
            s_instance.RefreshAndFocus();
        }
    }

    public static void ShowUpdateSuccess(string version)
    {
        EnsureInstance(UpdateState.Success);
        if (s_instance != null)
        {
            s_instance._targetVersion = version;
            s_instance._state = UpdateState.Success;
            s_instance._spinnerTimer?.Stop();
            s_instance.RefreshAndFocus();
        }
    }

    public static void CloseIfOpen()
    {
        try
        {
            if (s_instance != null && !s_instance.IsDisposed)
            {
                s_instance.Close();
            }
        }
        catch
        {
        }
    }

    private void RefreshAndFocus()
    {
        Invalidate();
        BringToFront();
        Activate();
    }

    private static void EnsureInstance(UpdateState state)
    {
        if (s_instance != null && !s_instance.IsDisposed)
        {
            return;
        }

        string cur = Updater.CurrentVersion.ToString(3);
        s_instance = new MacUpdateWindow(cur, state);
        s_instance.Show();
    }

    /// <summary>自检与文档离线截图渲染辅助（透明圆角 Squircle 输出，零边角伪影）。</summary>
    internal static Bitmap RenderPreview(UpdateState state, bool dark, string? currentVersion = null, string targetVersion = "2.2.0")
    {
        currentVersion ??= Updater.CurrentVersion.ToString(3);
        using var win = new MacUpdateWindow(currentVersion, state, dark, animateFade: false)
        {
            _targetVersion = targetVersion,
            _detailMessage = "未能连接到更新服务器，请检查网络连接后重试。",
            _downloadedBytes = 128_000,
            _totalDownloadBytes = 181_175,
            _downloadProgress = 128_000f / 181_175f,
        };
        var bmp = new Bitmap(win.ClientSize.Width, win.ClientSize.Height, PixelFormat.Format32bppArgb);
        bmp.SetResolution(96, 96);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        win.PaintSurface(g, bmp.Width, bmp.Height, clipToSquircle: true);
        return bmp;
    }
}
