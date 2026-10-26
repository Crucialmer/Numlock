using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Numlock;

/// <summary>
/// macOS 26 风格独立“软件更新”模态卡片窗口：
/// · 居中呈现于用户当前操作的显示器，取代 Windows 系统托盘通知（Toast）
/// · 连续曲率圆角矩形（Squircle）+ 极细半透明发丝描边 + 瞬态亚克力材质（DWM Transient Acrylic）
/// · macOS 经典红点关闭控制 + 顶部居中细致标题 + 全窗平滑拖拽移动
/// · 52px 品牌键帽磁贴应用图标 + 加粗品牌名 + 磨砂半透明版本徽标胶囊
/// · 动态状态内嵌卡片（macOS Inset Card）：
///     1. 检查中：流体 12 辐条 macOS 旋转加载指示器（Spinner）
///     2. 已是最新：Apple 经典翠绿圆角徽标 + 白色对勾矢量 + 检查时间戳
///     3. 发现更新：Apple 系统蓝下载徽标 + 优雅版本号对比 + 发布日志直达
///     4. 检查失败：Apple 暖橙警告徽章 + 友好原因说明 + 一键重试 / 网页直达
/// · Apple 原厂标准胶囊交互按钮（Accent Blue 高亮主按钮 / 次级微光灰按钮）
/// · 沉浸式深/浅双模自适应 + 键盘交互（Esc 关闭 / Enter 确认主操作）
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
    private int _cornerRadius;

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

    // 动画计时器
    private readonly System.Windows.Forms.Timer? _animTimer;
    private readonly System.Windows.Forms.Timer? _spinnerTimer;
    private double _targetOpacity = 1.0;
    private int _spinnerStep;

    // macOS 26 设计配色体系
    private Color BgColor => _dark ? Color.FromArgb(255, 32, 32, 36) : Color.FromArgb(255, 242, 242, 244);
    private Color FgColor => _dark ? Color.FromArgb(255, 245, 245, 247) : Color.FromArgb(255, 29, 29, 31);
    private Color SecondaryFg => _dark ? Color.FromArgb(170, 245, 245, 247) : Color.FromArgb(145, 29, 29, 31);
    private Color MutedFg => _dark ? Color.FromArgb(120, 245, 245, 247) : Color.FromArgb(115, 29, 29, 31);
    private Color AccentBlue => _dark ? Color.FromArgb(255, 10, 132, 255) : Color.FromArgb(255, 0, 122, 255);
    private Color AccentBlueHover => _dark ? Color.FromArgb(255, 30, 144, 255) : Color.FromArgb(255, 20, 132, 255);
    private Color AccentBluePressed => _dark ? Color.FromArgb(255, 8, 108, 212) : Color.FromArgb(255, 0, 106, 224);
    private Color SecondaryBtnBg => _dark ? Color.FromArgb(255, 58, 58, 62) : Color.FromArgb(255, 226, 226, 230);
    private Color SecondaryBtnHover => _dark ? Color.FromArgb(255, 70, 70, 76) : Color.FromArgb(255, 216, 216, 222);
    private Color SecondaryBtnPressed => _dark ? Color.FromArgb(255, 48, 48, 52) : Color.FromArgb(255, 206, 206, 212);
    private Color StrokeColor => _dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0);
    private Color CardBg => _dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(12, 0, 0, 0);

    // 状态色彩
    private Color SuccessGreen => Color.FromArgb(255, 48, 209, 88);
    private Color WarningAmber => Color.FromArgb(255, 255, 179, 0);

    public static bool IsVisible => s_instance != null && !s_instance.IsDisposed && s_instance.Visible;

    private MacUpdateWindow(string currentVersion, UpdateState initialState, bool? dark = null)
    {
        _currentVersion = currentVersion;
        _state = initialState;
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

        int width = (int)(390 * _scale);
        int height = (int)(380 * _scale);
        ClientSize = new Size(width, height);

        // 智能居中于用户当前操作光标所在的显示器工作区
        var screen = Screen.FromPoint(Cursor.Position);
        var wa = screen.WorkingArea;
        Location = new Point(
            wa.Left + Math.Max(0, (wa.Width - width) / 2),
            wa.Top + Math.Max(0, (wa.Height - height) / 2));

        // DWM 圆角与亚克力毛玻璃
        HandleCreated += (_, _) => UpdateWindowRegion();

        // 淡入动画
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

        // 检查中与下载中状态的 Spinner 旋转动画
        _spinnerTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _spinnerTimer.Tick += (_, _) =>
        {
            if (IsDisposed || Disposing)
            {
                _spinnerTimer?.Stop();
                return;
            }
            if (_state == UpdateState.Checking || _state == UpdateState.Downloading)
            {
                _spinnerStep = (_spinnerStep + 1) % 12;
                Invalidate();
            }
            else
            {
                _spinnerTimer.Stop();
            }
        };

        if (_state == UpdateState.Checking || _state == UpdateState.Downloading)
        {
            _spinnerTimer.Start();
        }
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
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        int w = ClientSize.Width;
        int h = ClientSize.Height;

        // 1. 面板底色
        using (var bgBrush = new SolidBrush(BgColor))
        {
            g.FillRectangle(bgBrush, ClientRectangle);
        }

        // 2. macOS 顶部标题栏控制（左上角红点 + 居中标题）
        float dotSize = 12 * _scale;
        float dotX = 16 * _scale;
        float dotY = 14 * _scale;
        _closeBtnRect = new RectangleF(dotX - 3, dotY - 3, dotSize + 6, dotSize + 6);

        Color dotColor = _closePressed
            ? Color.FromArgb(255, 220, 60, 52)
            : (_closeHover ? Color.FromArgb(255, 255, 110, 102) : Color.FromArgb(255, 255, 95, 86));

        using (var dotBrush = new SolidBrush(dotColor))
        {
            g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
        }

        if (_closeHover)
        {
            using var xPen = new Pen(Color.FromArgb(180, 80, 0, 0), 1.2f * _scale);
            float cx = dotX + dotSize / 2f;
            float cy = dotY + dotSize / 2f;
            float off = dotSize * 0.22f;
            g.DrawLine(xPen, cx - off, cy - off, cx + off, cy + off);
            g.DrawLine(xPen, cx + off, cy - off, cx - off, cy + off);
        }

        using (var titleFont = new Font("Microsoft YaHei", 12.5f * _scale, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            DrawStringCenter(g, "软件更新", titleFont, SecondaryFg, new RectangleF(0, 12 * _scale, w, 20 * _scale));
        }

        // 3. 应用标识（居中大磁贴 + 品牌名 + 当前版本徽标）
        int iconSize = (int)(52 * _scale);
        int iconX = (w - iconSize) / 2;
        int iconY = (int)(42 * _scale);
        using (var tileBmp = IconFactory.RenderAppTile(iconSize))
        {
            g.DrawImage(tileBmp, iconX, iconY, iconSize, iconSize);
        }

        using var nameFont = new Font("Microsoft YaHei", 20f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        var nameSize = g.MeasureString("Numlock", nameFont);
        int nameY = iconY + iconSize + (int)(6 * _scale);
        DrawStringCenter(g, "Numlock", nameFont, FgColor, new RectangleF(0, nameY, w, nameSize.Height));

        string verBadge = "当前版本 v" + _currentVersion;
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

        // 4. macOS 内嵌状态信息卡片（Inset Card）
        int cardMargin = (int)(20 * _scale);
        int cardX = cardMargin;
        int cardY = badgeY + badgeH + (int)(14 * _scale);
        int cardW = w - cardMargin * 2;
        int cardH = (int)(140 * _scale);

        var cardRect = new RectangleF(cardX, cardY, cardW, cardH);
        using (var cardPath = Gfx.RoundedRect(cardRect, 10 * _scale))
        {
            using var cardBrush = new SolidBrush(CardBg);
            g.FillPath(cardBrush, cardPath);
            using var cardStroke = new Pen(StrokeColor, 1f);
            g.DrawPath(cardStroke, cardPath);
        }

        // 根据当前状态绘制卡片内容
        _linkRect = RectangleF.Empty;
        DrawCardContent(g, cardX, cardY, cardW, cardH);

        // 5. 底部操作按钮
        DrawActionButtons(g, w, h);

        // 6. 全窗 1px 半透明发丝描边
        using var strokePen = new Pen(StrokeColor, 1f);
        using var borderPath = Gfx.RoundedRect(new RectangleF(0.5f, 0.5f, w - 1f, h - 1f), _cornerRadius - 0.5f);
        g.DrawPath(strokePen, borderPath);
    }

    private void DrawCardContent(Graphics g, int x, int y, int w, int h)
    {
        switch (_state)
        {
            case UpdateState.Checking:
                DrawCheckingCard(g, x, y, w, h);
                break;
            case UpdateState.Available:
                DrawAvailableCard(g, x, y, w, h);
                break;
            case UpdateState.Downloading:
                DrawDownloadingCard(g, x, y, w, h);
                break;
            case UpdateState.UpToDate:
                DrawUpToDateCard(g, x, y, w, h);
                break;
            case UpdateState.UpdateReady:
                DrawUpdateReadyCard(g, x, y, w, h);
                break;
            case UpdateState.Failed:
                DrawFailedCard(g, x, y, w, h);
                break;
            case UpdateState.Success:
                DrawSuccessCard(g, x, y, w, h);
                break;
        }
    }

    private void DrawCheckingCard(Graphics g, int x, int y, int w, int h)
    {
        float cx = x + w / 2f;
        float cy = y + (int)(36 * _scale);

        // 绘制 macOS 原生 12 辐条 Spinner
        DrawMacSpinner(g, cx, cy, (int)(14 * _scale));

        using var headFont = new Font("Microsoft YaHei", 15.5f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        int textY = (int)(cy + 24 * _scale);
        DrawStringCenter(g, "正在检查更新…", headFont, FgColor, new RectangleF(x, textY, w, 24 * _scale));

        using var descFont = new Font("Microsoft YaHei", 12.5f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawStringCenter(g, "正在连接 GitHub 服务器查询最新发布版本…", descFont, SecondaryFg, new RectangleF(x, textY + 24 * _scale, w, 22 * _scale));
    }

    private void DrawAvailableCard(Graphics g, int x, int y, int w, int h)
    {
        float cx = x + w / 2f;
        int iconSize = (int)(32 * _scale);
        int iconY = y + (int)(14 * _scale);
        int iconX = (int)(cx - iconSize / 2f);

        // Apple 蓝下载徽标
        using (var fill = new SolidBrush(AccentBlue))
        {
            g.FillEllipse(fill, iconX, iconY, iconSize, iconSize);
        }
        using (var pen = new Pen(Color.White, 2.2f * _scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            float r = iconSize / 2f;
            float icx = iconX + r;
            float icy = iconY + r;
            g.DrawLine(pen, icx, icy - r * 0.42f, icx, icy + r * 0.22f);
            g.DrawLines(pen, new[]
            {
                new PointF(icx - r * 0.32f, icy - r * 0.04f),
                new PointF(icx, icy + r * 0.28f),
                new PointF(icx + r * 0.32f, icy - r * 0.04f),
            });
            g.DrawLine(pen, icx - r * 0.42f, icy + r * 0.52f, icx + r * 0.42f, icy + r * 0.52f);
        }

        using var headFont = new Font("Microsoft YaHei", 15.5f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        int textY = iconY + iconSize + (int)(8 * _scale);
        DrawStringCenter(g, $"发现新版本 Numlock v{_targetVersion}", headFont, FgColor, new RectangleF(x, textY, w, 22 * _scale));

        using var descFont = new Font("Microsoft YaHei", 11.5f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        string sizeStr = _targetSizeBytes > 0 ? $"，新包约 {FormatSize(_targetSizeBytes)}" : "";
        DrawStringCenter(g, $"发现新版本可用（当前运行的是 v{_currentVersion}{sizeStr}）。点击下方按钮开始下载。", descFont, SecondaryFg,
            new RectangleF(x + 10 * _scale, textY + 22 * _scale, w - 20 * _scale, 32 * _scale), wordBreak: true);

        // 发布说明链接
        string linkText = "查看 GitHub Release 发布说明 →";
        using var linkFont = new Font("Microsoft YaHei", 11.5f * _scale, _linkHover ? FontStyle.Underline : FontStyle.Regular, GraphicsUnit.Pixel);
        var linkSize = g.MeasureString(linkText, linkFont);
        int linkX = (int)(cx - linkSize.Width / 2f);
        int linkY = textY + (int)(54 * _scale);
        _linkRect = new RectangleF(linkX, linkY, linkSize.Width, linkSize.Height);

        DrawStringCenter(g, linkText, linkFont, _linkHover ? AccentBlueHover : AccentBlue, _linkRect);
    }

    private void DrawDownloadingCard(Graphics g, int x, int y, int w, int h)
    {
        float cx = x + w / 2f;
        float cy = y + (int)(24 * _scale);

        // 绘制 macOS 原生 12 辐条 Spinner
        DrawMacSpinner(g, cx, cy, (int)(11 * _scale));

        using var headFont = new Font("Microsoft YaHei", 15f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        int titleY = (int)(cy + 18 * _scale);
        DrawStringCenter(g, "正在下载更新…", headFont, FgColor, new RectangleF(x, titleY, w, 22 * _scale));

        // 绘制 macOS 风格细长胶囊圆角进度条
        int barH = Math.Max(4, (int)(6 * _scale));
        int barMarginX = (int)(28 * _scale);
        int barW = w - barMarginX * 2;
        int barX = x + barMarginX;
        int barY = titleY + (int)(28 * _scale);

        var trackRect = new RectangleF(barX, barY, barW, barH);
        float barRadius = barH / 2f;

        // 底槽轨道
        using (var trackPath = Gfx.RoundedRect(trackRect, barRadius))
        {
            using var trackBrush = new SolidBrush(Color.FromArgb(_dark ? 55 : 30, FgColor));
            g.FillPath(trackBrush, trackPath);
        }

        // 进度填充
        if (_downloadProgress > 0f)
        {
            float fillW = Math.Clamp(barW * _downloadProgress, barH, barW);
            var fillRect = new RectangleF(barX, barY, fillW, barH);
            using var fillPath = Gfx.RoundedRect(fillRect, barRadius);
            using var fillBrush = new SolidBrush(AccentBlue);
            g.FillPath(fillBrush, fillPath);
        }

        // 实时下载量与百分比文字（小包自适应 KB/MB 智能显示）
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

        using var descFont = new Font("Microsoft YaHei", 11.5f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        int textY = barY + barH + (int)(8 * _scale);
        DrawStringCenter(g, progressText, descFont, SecondaryFg, new RectangleF(x, textY, w, 20 * _scale));

        using var hintFont = new Font("Microsoft YaHei", 10.5f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawStringCenter(g, "下载完成将自动校验 SHA-256 并准备就绪", hintFont, MutedFg, new RectangleF(x, textY + 20 * _scale, w, 18 * _scale));
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

    private void DrawMacSpinner(Graphics g, float cx, float cy, float radius)
    {
        float innerR = radius * 0.48f;
        float outerR = radius;
        for (int i = 0; i < 12; i++)
        {
            int diff = (i - _spinnerStep + 12) % 12;
            int alpha = (int)(45 + 210 * (1.0f - diff / 12.0f));
            Color spokeColor = Color.FromArgb(alpha, FgColor);

            double angle = i * Math.PI / 6.0;
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);

            PointF p1 = new(cx + innerR * cos, cy + innerR * sin);
            PointF p2 = new(cx + outerR * cos, cy + outerR * sin);

            using var pen = new Pen(spokeColor, 2.3f * _scale)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLine(pen, p1, p2);
        }
    }

    private void DrawUpToDateCard(Graphics g, int x, int y, int w, int h)
    {
        float cx = x + w / 2f;
        int iconSize = (int)(32 * _scale);
        int iconY = y + (int)(16 * _scale);
        int iconX = (int)(cx - iconSize / 2f);

        // Apple 绿色对勾徽标
        using (var fill = new SolidBrush(SuccessGreen))
        {
            g.FillEllipse(fill, iconX, iconY, iconSize, iconSize);
        }
        using (var pen = new Pen(Color.White, 2.2f * _scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            float r = iconSize / 2f;
            float icx = iconX + r;
            float icy = iconY + r;
            g.DrawLines(pen, new[]
            {
                new PointF(icx - r * 0.45f, icy + r * 0.05f),
                new PointF(icx - r * 0.12f, icy + r * 0.38f),
                new PointF(icx + r * 0.45f, icy - r * 0.35f),
            });
        }

        using var headFont = new Font("Microsoft YaHei", 15.5f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        int textY = iconY + iconSize + (int)(10 * _scale);
        DrawStringCenter(g, "您使用的是最新版本", headFont, FgColor, new RectangleF(x, textY, w, 22 * _scale));

        using var descFont = new Font("Microsoft YaHei", 12.5f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawStringCenter(g, $"Numlock {_currentVersion} 是目前最新的可用版本。", descFont, SecondaryFg, new RectangleF(x, textY + 24 * _scale, w, 20 * _scale));

        using var timeFont = new Font("Microsoft YaHei", 11.5f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawStringCenter(g, $"最后检查：今天 {_checkTime:HH:mm:ss}", timeFont, MutedFg, new RectangleF(x, textY + 44 * _scale, w, 18 * _scale));
    }

    private void DrawUpdateReadyCard(Graphics g, int x, int y, int w, int h)
    {
        float cx = x + w / 2f;
        int iconSize = (int)(32 * _scale);
        int iconY = y + (int)(14 * _scale);
        int iconX = (int)(cx - iconSize / 2f);

        // Apple 蓝下载徽标
        using (var fill = new SolidBrush(AccentBlue))
        {
            g.FillEllipse(fill, iconX, iconY, iconSize, iconSize);
        }
        using (var pen = new Pen(Color.White, 2.2f * _scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            float r = iconSize / 2f;
            float icx = iconX + r;
            float icy = iconY + r;
            g.DrawLine(pen, icx, icy - r * 0.42f, icx, icy + r * 0.22f);
            g.DrawLines(pen, new[]
            {
                new PointF(icx - r * 0.32f, icy - r * 0.04f),
                new PointF(icx, icy + r * 0.28f),
                new PointF(icx + r * 0.32f, icy - r * 0.04f),
            });
            g.DrawLine(pen, icx - r * 0.42f, icy + r * 0.52f, icx + r * 0.42f, icy + r * 0.52f);
        }

        using var headFont = new Font("Microsoft YaHei", 15.5f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        int textY = iconY + iconSize + (int)(8 * _scale);
        DrawStringCenter(g, $"发现新版本 Numlock v{_targetVersion}", headFont, FgColor, new RectangleF(x, textY, w, 22 * _scale));

        using var descFont = new Font("Microsoft YaHei", 11.5f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawStringCenter(g, "新版本已下载就绪；退出时将自动替换，也可立即重启生效。", descFont, SecondaryFg,
            new RectangleF(x + 10 * _scale, textY + 22 * _scale, w - 20 * _scale, 32 * _scale), wordBreak: true);

        // 发布说明链接
        string linkText = "查看 GitHub Release 发布说明 →";
        using var linkFont = new Font("Microsoft YaHei", 11.5f * _scale, _linkHover ? FontStyle.Underline : FontStyle.Regular, GraphicsUnit.Pixel);
        var linkSize = g.MeasureString(linkText, linkFont);
        int linkX = (int)(cx - linkSize.Width / 2f);
        int linkY = textY + (int)(54 * _scale);
        _linkRect = new RectangleF(linkX, linkY, linkSize.Width, linkSize.Height);

        DrawStringCenter(g, linkText, linkFont, _linkHover ? AccentBlueHover : AccentBlue, _linkRect);
    }

    private void DrawFailedCard(Graphics g, int x, int y, int w, int h)
    {
        float cx = x + w / 2f;
        int iconSize = (int)(32 * _scale);
        int iconY = y + (int)(14 * _scale);
        int iconX = (int)(cx - iconSize / 2f);

        // Apple 暖橙警告徽标
        using (var fill = new SolidBrush(WarningAmber))
        {
            g.FillEllipse(fill, iconX, iconY, iconSize, iconSize);
        }
        using (var pen = new Pen(Color.White, 2.3f * _scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            float r = iconSize / 2f;
            float icx = iconX + r;
            float icy = iconY + r;
            g.DrawLine(pen, icx, icy - r * 0.35f, icx, icy + r * 0.12f);
            float dotR = 1.3f * _scale;
            using var dotBrush = new SolidBrush(Color.White);
            g.FillEllipse(dotBrush, icx - dotR, icy + r * 0.38f - dotR, dotR * 2, dotR * 2);
        }

        using var headFont = new Font("Microsoft YaHei", 15.5f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        int textY = iconY + iconSize + (int)(8 * _scale);
        DrawStringCenter(g, "无法检查更新", headFont, FgColor, new RectangleF(x, textY, w, 22 * _scale));

        string msg = string.IsNullOrWhiteSpace(_detailMessage) ? "未能连接到更新服务器，请检查网络连接后重试。" : _detailMessage;
        using var descFont = new Font("Microsoft YaHei", 11.5f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawStringCenter(g, msg, descFont, SecondaryFg, new RectangleF(x + 14 * _scale, textY + 22 * _scale, w - 28 * _scale, 32 * _scale), wordBreak: true);

        string linkText = "前往 GitHub 发布页手动下载 →";
        using var linkFont = new Font("Microsoft YaHei", 11.5f * _scale, _linkHover ? FontStyle.Underline : FontStyle.Regular, GraphicsUnit.Pixel);
        var linkSize = g.MeasureString(linkText, linkFont);
        int linkX = (int)(cx - linkSize.Width / 2f);
        int linkY = textY + (int)(54 * _scale);
        _linkRect = new RectangleF(linkX, linkY, linkSize.Width, linkSize.Height);

        DrawStringCenter(g, linkText, linkFont, _linkHover ? AccentBlueHover : AccentBlue, _linkRect);
    }

    private void DrawSuccessCard(Graphics g, int x, int y, int w, int h)
    {
        float cx = x + w / 2f;
        int iconSize = (int)(32 * _scale);
        int iconY = y + (int)(16 * _scale);
        int iconX = (int)(cx - iconSize / 2f);

        using (var fill = new SolidBrush(SuccessGreen))
        {
            g.FillEllipse(fill, iconX, iconY, iconSize, iconSize);
        }
        using (var pen = new Pen(Color.White, 2.2f * _scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            float r = iconSize / 2f;
            float icx = iconX + r;
            float icy = iconY + r;
            g.DrawLines(pen, new[]
            {
                new PointF(icx - r * 0.45f, icy + r * 0.05f),
                new PointF(icx - r * 0.12f, icy + r * 0.38f),
                new PointF(icx + r * 0.45f, icy - r * 0.35f),
            });
        }

        using var headFont = new Font("Microsoft YaHei", 15.5f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        int textY = iconY + iconSize + (int)(10 * _scale);
        DrawStringCenter(g, "更新已成功完成", headFont, FgColor, new RectangleF(x, textY, w, 22 * _scale));

        using var descFont = new Font("Microsoft YaHei", 12.5f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        DrawStringCenter(g, $"已成功升级到 v{_targetVersion}，您目前运行的是最新版本。", descFont, SecondaryFg, new RectangleF(x, textY + 24 * _scale, w, 20 * _scale));
    }

    private void DrawActionButtons(Graphics g, int w, int h)
    {
        int btnH = (int)(32 * _scale);
        int btnY = h - (int)(18 * _scale) - btnH;
        int padX = (int)(20 * _scale);

        _primaryBtnRect = RectangleF.Empty;
        _secondaryBtnRect = RectangleF.Empty;

        using var btnFont = new Font("Microsoft YaHei", 14f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        using var secFont = new Font("Microsoft YaHei", 14f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);

        if (_state == UpdateState.Checking || _state == UpdateState.Downloading)
        {
            // 单按钮：取消
            int btnW = (int)(92 * _scale);
            int btnX = (w - btnW) / 2;
            _secondaryBtnRect = new RectangleF(btnX, btnY, btnW, btnH);

            DrawPillButton(g, _secondaryBtnRect, "取消", secFont,
                _secondaryPressed ? SecondaryBtnPressed : (_secondaryHover ? SecondaryBtnHover : SecondaryBtnBg),
                FgColor);
        }
        else if (_state == UpdateState.Available)
        {
            // 双按钮：稍后 + 立即下载更新
            int priW = (int)(130 * _scale);
            int secW = (int)(76 * _scale);
            int gap = (int)(10 * _scale);

            int priX = w - padX - priW;
            int secX = priX - gap - secW;

            _secondaryBtnRect = new RectangleF(secX, btnY, secW, btnH);
            _primaryBtnRect = new RectangleF(priX, btnY, priW, btnH);

            DrawPillButton(g, _secondaryBtnRect, "稍后", secFont,
                _secondaryPressed ? SecondaryBtnPressed : (_secondaryHover ? SecondaryBtnHover : SecondaryBtnBg),
                FgColor);

            DrawPillButton(g, _primaryBtnRect, "立即下载更新", btnFont,
                _primaryPressed ? AccentBluePressed : (_primaryHover ? AccentBlueHover : AccentBlue),
                Color.White);
        }
        else if (_state == UpdateState.UpToDate || _state == UpdateState.Success)
        {
            // 单按钮：好（Accent Blue）
            int btnW = (int)(110 * _scale);
            int btnX = (w - btnW) / 2;
            _primaryBtnRect = new RectangleF(btnX, btnY, btnW, btnH);

            DrawPillButton(g, _primaryBtnRect, "好", btnFont,
                _primaryPressed ? AccentBluePressed : (_primaryHover ? AccentBlueHover : AccentBlue),
                Color.White);
        }
        else if (_state == UpdateState.UpdateReady)
        {
            // 双按钮：稍后 + 重启并更新
            int priW = (int)(120 * _scale);
            int secW = (int)(76 * _scale);
            int gap = (int)(10 * _scale);

            int priX = w - padX - priW;
            int secX = priX - gap - secW;

            _secondaryBtnRect = new RectangleF(secX, btnY, secW, btnH);
            _primaryBtnRect = new RectangleF(priX, btnY, priW, btnH);

            DrawPillButton(g, _secondaryBtnRect, "稍后", secFont,
                _secondaryPressed ? SecondaryBtnPressed : (_secondaryHover ? SecondaryBtnHover : SecondaryBtnBg),
                FgColor);

            DrawPillButton(g, _primaryBtnRect, "重启并更新", btnFont,
                _primaryPressed ? AccentBluePressed : (_primaryHover ? AccentBlueHover : AccentBlue),
                Color.White);
        }
        else if (_state == UpdateState.Failed)
        {
            // 双按钮：好 + 重试
            int priW = (int)(88 * _scale);
            int secW = (int)(76 * _scale);
            int gap = (int)(10 * _scale);

            int priX = w - padX - priW;
            int secX = priX - gap - secW;

            _secondaryBtnRect = new RectangleF(secX, btnY, secW, btnH);
            _primaryBtnRect = new RectangleF(priX, btnY, priW, btnH);

            DrawPillButton(g, _secondaryBtnRect, "好", secFont,
                _secondaryPressed ? SecondaryBtnPressed : (_secondaryHover ? SecondaryBtnHover : SecondaryBtnBg),
                FgColor);

            DrawPillButton(g, _primaryBtnRect, "重试", btnFont,
                _primaryPressed ? AccentBluePressed : (_primaryHover ? AccentBlueHover : AccentBlue),
                Color.White);
        }
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
            catch { }
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
                var dlAction = _onDownload;
                dlAction?.Invoke();
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
                _onCancelDownload?.Invoke();
                Close();
                break;
        }
    }

    private void TriggerRetry()
    {
        _state = UpdateState.Checking;
        _spinnerStep = 0;
        _spinnerTimer?.Start();
        Invalidate();
        _onRetry?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_state == UpdateState.Downloading)
            {
                _onCancelDownload?.Invoke();
            }
            if (s_instance == this)
            {
                s_instance = null;
            }
            _spinnerTimer?.Stop();
            _spinnerTimer?.Dispose();
            _animTimer?.Stop();
            _animTimer?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_state == UpdateState.Downloading)
        {
            _onCancelDownload?.Invoke();
        }
        if (s_instance == this)
        {
            s_instance = null;
        }
        _spinnerTimer?.Stop();
        _spinnerTimer?.Dispose();
        _animTimer?.Stop();
        _animTimer?.Dispose();
        base.OnFormClosed(e);
    }

    // ——— 静态公共调用入口 ———

    /// <summary>打开或激活检查中状态的更新窗口。</summary>
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
            s_instance.Invalidate();
            s_instance.BringToFront();
            s_instance.Activate();
        }
    }

    /// <summary>更新结果：发现新版本（待用户确认是否下载）。</summary>
    public static void SetResultAvailable(string version, Action onDownload, string pageUrl = Updater.PageUrl, long targetSizeBytes = 0)
    {
        if (s_instance != null && !s_instance.IsDisposed)
        {
            s_instance._targetVersion = version;
            s_instance._onDownload = onDownload;
            s_instance._pageUrl = pageUrl;
            s_instance._targetSizeBytes = targetSizeBytes;
            s_instance._state = UpdateState.Available;
            s_instance._spinnerTimer?.Stop();
            s_instance.Invalidate();
            s_instance.BringToFront();
            s_instance.Activate();
        }
        else
        {
            ShowAvailable(version, onDownload, pageUrl, targetSizeBytes);
        }
    }

    /// <summary>直接以“发现新版本”状态打开窗口。</summary>
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
            s_instance.Invalidate();
            s_instance.BringToFront();
            s_instance.Activate();
        }
    }

    /// <summary>切换为“正在下载更新”状态。</summary>
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
            s_instance.Invalidate();
            s_instance.BringToFront();
            s_instance.Activate();
        }
    }

    /// <summary>更新下载进度（线程安全，自动分发到 UI 线程）。</summary>
    public static void UpdateProgress(long downloaded, long total)
    {
        if (s_instance != null && !s_instance.IsDisposed)
        {
            if (s_instance.InvokeRequired)
            {
                try
                {
                    s_instance.BeginInvoke(new Action(() => UpdateProgress(downloaded, total)));
                }
                catch { }
                return;
            }

            if (s_instance._state == UpdateState.Downloading)
            {
                s_instance._downloadedBytes = downloaded;
                s_instance._totalDownloadBytes = total;
                if (total > 0)
                {
                    s_instance._downloadProgress = Math.Clamp((float)downloaded / total, 0f, 1f);
                }
                else
                {
                    s_instance._downloadProgress = -1f;
                }
                s_instance.Invalidate();
            }
        }
    }

    /// <summary>更新结果：已是最新版本。</summary>
    public static void SetResultUpToDate(string version)
    {
        if (s_instance != null && !s_instance.IsDisposed)
        {
            s_instance._currentVersion = version;
            s_instance._checkTime = DateTime.Now;
            s_instance._state = UpdateState.UpToDate;
            s_instance._spinnerTimer?.Stop();
            s_instance.Invalidate();
            s_instance.BringToFront();
            s_instance.Activate();
        }
        else
        {
            ShowUpToDate(version);
        }
    }

    /// <summary>更新结果：新版本已就绪。</summary>
    public static void SetResultReady(string version, Action onRestart, string pageUrl = Updater.PageUrl)
    {
        if (s_instance != null && !s_instance.IsDisposed)
        {
            s_instance._targetVersion = version;
            s_instance._onRestart = onRestart;
            s_instance._pageUrl = pageUrl;
            s_instance._state = UpdateState.UpdateReady;
            s_instance._spinnerTimer?.Stop();
            s_instance.Invalidate();
            s_instance.BringToFront();
            s_instance.Activate();
        }
        else
        {
            ShowReady(version, onRestart, pageUrl);
        }
    }

    /// <summary>更新结果：检查失败。</summary>
    public static void SetResultFailed(string detail, Action? onRetry, string pageUrl = Updater.PageUrl)
    {
        if (s_instance != null && !s_instance.IsDisposed)
        {
            s_instance._detailMessage = detail;
            s_instance._onRetry = onRetry;
            s_instance._pageUrl = pageUrl;
            s_instance._state = UpdateState.Failed;
            s_instance._spinnerTimer?.Stop();
            s_instance.Invalidate();
            s_instance.BringToFront();
            s_instance.Activate();
        }
        else
        {
            ShowFailed(detail, onRetry, pageUrl);
        }
    }

    /// <summary>直接以“已是最新版本”状态打开窗口。</summary>
    public static void ShowUpToDate(string version)
    {
        EnsureInstance(UpdateState.UpToDate);
        if (s_instance != null)
        {
            s_instance._currentVersion = version;
            s_instance._checkTime = DateTime.Now;
            s_instance._state = UpdateState.UpToDate;
            s_instance.Invalidate();
            s_instance.BringToFront();
            s_instance.Activate();
        }
    }

    /// <summary>直接以“新版本已就绪”状态打开窗口。</summary>
    public static void ShowReady(string version, Action onRestart, string pageUrl = Updater.PageUrl)
    {
        EnsureInstance(UpdateState.UpdateReady);
        if (s_instance != null)
        {
            s_instance._targetVersion = version;
            s_instance._onRestart = onRestart;
            s_instance._pageUrl = pageUrl;
            s_instance._state = UpdateState.UpdateReady;
            s_instance.Invalidate();
            s_instance.BringToFront();
            s_instance.Activate();
        }
    }

    /// <summary>直接以“检查失败”状态打开窗口。</summary>
    public static void ShowFailed(string detail, Action? onRetry, string pageUrl = Updater.PageUrl)
    {
        EnsureInstance(UpdateState.Failed);
        if (s_instance != null)
        {
            s_instance._detailMessage = detail;
            s_instance._onRetry = onRetry;
            s_instance._pageUrl = pageUrl;
            s_instance._state = UpdateState.Failed;
            s_instance.Invalidate();
            s_instance.BringToFront();
            s_instance.Activate();
        }
    }

    /// <summary>升级完成后展示升级成功卡片。</summary>
    public static void ShowUpdateSuccess(string version)
    {
        EnsureInstance(UpdateState.Success);
        if (s_instance != null)
        {
            s_instance._targetVersion = version;
            s_instance._state = UpdateState.Success;
            s_instance.Invalidate();
            s_instance.BringToFront();
            s_instance.Activate();
        }
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

    /// <summary>自检与文档离线截图渲染辅助：无需实际弹出窗口即可离线精确渲染各状态外观。</summary>
    internal static Bitmap RenderPreview(UpdateState state, bool dark, string currentVersion = "1.5.4", string targetVersion = "1.6.0")
    {
        using var win = new MacUpdateWindow(currentVersion, state, dark)
        {
            _targetVersion = targetVersion,
            _detailMessage = "未能连接到更新服务器，请检查网络连接后重试。",
            _downloadedBytes = 18_454_937,
            _totalDownloadBytes = 43_599_074,
            _downloadProgress = 18_454_937f / 43_599_074f,
        };
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
