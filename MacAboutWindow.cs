using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Numlock;

/// <summary>
/// macOS 26 (Tahoe) Liquid Glass 独立“关于软件”紧凑型模态卡片窗口：
/// · 液态玻璃光学渐变面板 + 顶部镜面高光折射边 + DWM 瞬态亚克力材质
/// · 同心连续曲率圆角（Concentric Squircle）+ 经典交通灯红点关闭控制 + 全窗无闪烁拖拽
/// · 52px 立体浮雕键帽应用图标 + 品牌名 + 磨砂玻璃版本徽标胶囊 + 副标题
/// · 液态玻璃内嵌图例卡片 + 全胶囊交互主按钮（Full Pill Capsule）
/// · 纯 GDI+ 灰度抗锯齿渲染 + 字体/磁贴缓存（悬停零分配）+ 全 DPI 漫游适配（100%–400% / 8K）
/// </summary>
internal sealed class MacAboutWindow : Form
{
    private static MacAboutWindow? s_instance;

    private readonly bool _dark;
    private readonly string _version;
    private float _scale;
    private bool _dwmRounded;

    private RectangleF _closeBtnRect;
    private RectangleF _actionBtnRect;
    private RectangleF _linkRect;

    private bool _closeHover;
    private bool _closePressed;
    private bool _actionHover;
    private bool _actionPressed;
    private bool _linkHover;

    private System.Windows.Forms.Timer? _animTimer;

    // 按 DPI 缓存的字体，避免悬停/重绘反复分配 GDI+ 对象
    private Font? _topFont;
    private Font? _nameFont;
    private Font? _verFont;
    private Font? _subFont;
    private Font? _labelFont;
    private Font? _descFont;
    private Font? _linkFont;
    private Font? _linkHoverFont;
    private Font? _btnFont;

    private LiquidGlass.Palette Pal => LiquidGlass.Get(_dark);

    public static bool IsVisible => s_instance != null && !s_instance.IsDisposed && s_instance.Visible;

    internal MacAboutWindow(string? version = null, bool? dark = null, bool animateFade = true)
    {
        _version = string.IsNullOrWhiteSpace(version)
            ? Updater.CurrentVersion.ToString(3)
            : version.TrimStart('v', 'V');
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

        int width = (int)Math.Round(328 * _scale);
        int height = (int)Math.Round(430 * _scale);
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
        }
    }

    private void RebuildFonts()
    {
        DisposeFonts();
        _topFont = LiquidGlass.CreateFont(12f * _scale, FontStyle.Regular);
        _nameFont = LiquidGlass.CreateFont(21f * _scale, FontStyle.Bold);
        _verFont = LiquidGlass.CreateFont(11.5f * _scale, FontStyle.Bold);
        _subFont = LiquidGlass.CreateFont(12f * _scale, FontStyle.Regular);
        _labelFont = LiquidGlass.CreateFont(13f * _scale, FontStyle.Bold);
        _descFont = LiquidGlass.CreateFont(12f * _scale, FontStyle.Regular);
        _linkFont = LiquidGlass.CreateFont(12f * _scale, FontStyle.Regular);
        _linkHoverFont = LiquidGlass.CreateFont(12f * _scale, FontStyle.Underline);
        _btnFont = LiquidGlass.CreateFont(13.5f * _scale, FontStyle.Bold);
    }

    private void DisposeFonts()
    {
        _topFont?.Dispose();
        _nameFont?.Dispose();
        _verFont?.Dispose();
        _subFont?.Dispose();
        _labelFont?.Dispose();
        _descFont?.Dispose();
        _linkFont?.Dispose();
        _linkHoverFont?.Dispose();
        _btnFont?.Dispose();
        _topFont = _nameFont = _verFont = _subFont = _labelFont = _descFont = _linkFont = _linkHoverFont = _btnFont = null;
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
        PaintSurface(e.Graphics, ClientSize.Width, ClientSize.Height, clipToSquircle: false);
    }

    private void PaintSurface(Graphics g, int w, int h, bool clipToSquircle)
    {
        LiquidGlass.ConfigureGraphics(g);
        var p = Pal;

        // 运行时 Win11 DWM 圆角为 8 DIP；离线预览或回退模式用 16 DIP 连续曲率方圆角
        float borderRadius = clipToSquircle || !_dwmRounded ? 16f * _scale : 8f * _scale;
        var outerBounds = new RectangleF(0, 0, w, h);

        using var clipPath = clipToSquircle ? Gfx.RoundedRect(outerBounds, borderRadius) : null;
        LiquidGlass.FillWindowSurface(g, outerBounds, p, clipPath);

        // 1. macOS 顶部标题栏控制（交通灯红点 + 居中细致标题）
        float dotSize = 12f * _scale;
        float dotX = 14f * _scale;
        float dotY = 13f * _scale;
        _closeBtnRect = new RectangleF(dotX - 3, dotY - 3, dotSize + 6, dotSize + 6);
        LiquidGlass.DrawTrafficClose(g, dotX, dotY, dotSize, _scale, _closeHover, _closePressed);

        LiquidGlass.DrawTextCenter(g, "关于软件", _topFont!, p.TextSecondary, new RectangleF(0, 11 * _scale, w, 18 * _scale));

        // 2. 应用磁贴 + 品牌名 + 液态玻璃版本徽标胶囊 + 副标题
        int iconSize = (int)Math.Round(52 * _scale);
        int iconX = (w - iconSize) / 2;
        int iconY = (int)Math.Round(36 * _scale);
        LiquidGlass.DrawCachedAppTile(g, new RectangleF(iconX, iconY, iconSize, iconSize), iconSize * 2);

        var nameSize = g.MeasureString("Numlock", _nameFont!);
        int nameY = iconY + iconSize + (int)Math.Round(6 * _scale);
        LiquidGlass.DrawTextCenter(g, "Numlock", _nameFont!, p.TextPrimary, new RectangleF(0, nameY, w, nameSize.Height));

        string verBadge = "v" + _version;
        var verSize = LiquidGlass.MeasureTightText(g, verBadge, _verFont!);
        int badgeW = (int)Math.Ceiling(verSize.Width) + (int)Math.Round(22 * _scale);
        int badgeH = (int)Math.Ceiling(verSize.Height) + (int)Math.Round(6 * _scale);
        int badgeX = (w - badgeW) / 2;
        int badgeY = nameY + (int)Math.Ceiling(nameSize.Height) + (int)Math.Round(4 * _scale);
        LiquidGlass.DrawVersionBadge(g, new RectangleF(badgeX, badgeY, badgeW, badgeH), verBadge, _verFont!, p);

        int subY = badgeY + badgeH + (int)Math.Round(6 * _scale);
        LiquidGlass.DrawTextCenter(g, "数字小键盘状态看护 · 保持常开", _subFont!, p.TextSecondary, new RectangleF(0, subY, w, 18 * _scale));

        // 3. Liquid Glass 内嵌图例卡片（同心圆角 12px，与副标题保持充裕呼吸感）
        int cardMargin = (int)Math.Round(16 * _scale);
        int cardX = cardMargin;
        int cardY = subY + (int)Math.Round(24 * _scale);
        int cardW = w - cardMargin * 2;
        int cardH = (int)Math.Round(148 * _scale);
        LiquidGlass.DrawInsetCard(g, new RectangleF(cardX, cardY, cardW, cardH), 12f * _scale, p);

        int rowH = (int)Math.Round(33 * _scale);
        int startRowY = cardY + (int)Math.Round(8 * _scale);
        int rowPadX = cardX + (int)Math.Round(12 * _scale);
        int cardRight = cardX + cardW;

        DrawLegendRow(g, rowPadX, startRowY + rowH * 0, cardRight, "实心键帽", "·  已开启（NumLock ON）", isSolid: true, hasBadge: false);
        DrawLegendRow(g, rowPadX, startRowY + rowH * 1, cardRight, "描边键帽", "·  已关闭（NumLock OFF）", isSolid: false, hasBadge: false);
        DrawLegendRow(g, rowPadX, startRowY + rowH * 2, cardRight, "绿色圆点", "·  保持常开模式已激活", isSolid: true, hasBadge: true);
        DrawActionRow(g, rowPadX, startRowY + rowH * 3, cardRight, "快捷交互", "·  双击切换 · 右键菜单");

        // 4. 主页链接与 macOS 26 全胶囊主按钮
        const string linkText = "GitHub: Crucialmer/Numlock";
        Font activeLinkFont = _linkHover ? _linkHoverFont! : _linkFont!;
        var linkSize = g.MeasureString(linkText, activeLinkFont);
        int linkY = cardY + cardH + (int)Math.Round(12 * _scale);
        int linkX = (int)Math.Round((w - linkSize.Width) / 2f);
        _linkRect = new RectangleF(linkX, linkY, linkSize.Width, linkSize.Height);
        LiquidGlass.DrawTextCenter(g, linkText, activeLinkFont, _linkHover ? p.AccentHoverTop : p.TextSecondary, _linkRect);

        int btnW = (int)Math.Round(104 * _scale);
        int btnH = (int)Math.Round(30 * _scale);
        int btnX = (w - btnW) / 2;
        int btnY = linkY + (int)Math.Ceiling(linkSize.Height) + (int)Math.Round(10 * _scale);
        _actionBtnRect = new RectangleF(btnX, btnY, btnW, btnH);

        LiquidGlass.DrawPillButton(g, _actionBtnRect, "好", _btnFont!, p, isPrimary: true, _actionHover, _actionPressed);

        // 5. 外围 Liquid Glass 顶缘镜面高光描边（与实际窗口圆角精确贴合）
        var rimRect = new RectangleF(0.5f, 0.5f, w - 1f, h - 1f);
        using var borderPath = Gfx.RoundedRect(rimRect, Math.Max(1f, borderRadius - 0.5f));
        LiquidGlass.DrawGlassRim(g, borderPath, rimRect, p, 1f);
    }

    private void DrawLegendRow(Graphics g, int x, int y, int cardRight, string label, string desc, bool isSolid, bool hasBadge)
    {
        var p = Pal;
        int sz = (int)Math.Round(16 * _scale);
        int rowH = (int)Math.Round(32 * _scale);
        int iconY = y + (rowH - sz) / 2;
        LiquidGlass.DrawCachedTrayIcon(g, new RectangleF(x, iconY, sz, sz), sz * 2, isSolid, _dark, hasBadge);

        int textX = x + sz + (int)Math.Round(8 * _scale);
        var labelSize = g.MeasureString(label, _labelFont!);
        LiquidGlass.DrawTextLeft(g, label, _labelFont!, p.TextPrimary, new RectangleF(textX, y, labelSize.Width, rowH));

        int descX = textX + (int)Math.Ceiling(labelSize.Width) + (int)Math.Round(4 * _scale);
        float descW = Math.Max(0, cardRight - descX - (int)Math.Round(8 * _scale));
        LiquidGlass.DrawTextLeft(g, desc, _descFont!, p.TextSecondary, new RectangleF(descX, y, descW, rowH));
    }

    private void DrawActionRow(Graphics g, int x, int y, int cardRight, string label, string desc)
    {
        var p = Pal;
        int sz = (int)Math.Round(16 * _scale);
        int rowH = (int)Math.Round(32 * _scale);
        float cx = x + sz / 2f;
        float cy = y + rowH / 2f;
        float dotR = 3.5f * _scale;
        using (var halo = new SolidBrush(Color.FromArgb(_dark ? 65 : 48, p.AccentSolid)))
        {
            g.FillEllipse(halo, cx - dotR * 1.6f, cy - dotR * 1.6f, dotR * 3.2f, dotR * 3.2f);
        }
        using (var dotBrush = new SolidBrush(p.AccentSolid))
        {
            g.FillEllipse(dotBrush, cx - dotR, cy - dotR, dotR * 2f, dotR * 2f);
        }

        int textX = x + sz + (int)Math.Round(8 * _scale);
        var labelSize = g.MeasureString(label, _labelFont!);
        LiquidGlass.DrawTextLeft(g, label, _labelFont!, p.TextPrimary, new RectangleF(textX, y, labelSize.Width, rowH));

        int descX = textX + (int)Math.Ceiling(labelSize.Width) + (int)Math.Round(4 * _scale);
        float descW = Math.Max(0, cardRight - descX - (int)Math.Round(8 * _scale));
        LiquidGlass.DrawTextLeft(g, desc, _descFont!, p.TextSecondary, new RectangleF(descX, y, descW, rowH));
    }

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
            catch
            {
            }
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
        if (s_instance == this)
        {
            s_instance = null;
        }
        LiquidGlass.DisposeTimer(ref _animTimer);
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
            LiquidGlass.DisposeTimer(ref _animTimer);
            DisposeFonts();
        }
        base.Dispose(disposing);
    }

    /// <summary>单例打开 macOS 26 风格“关于软件”紧凑型独立卡片窗口。</summary>
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

    /// <summary>自检与文档离线截图渲染辅助（透明圆角 Squircle 输出，零边角伪影）。</summary>
    internal static Bitmap RenderPreview(bool dark, string? version = null)
    {
        version ??= Updater.CurrentVersion.ToString(3);
        using var win = new MacAboutWindow(version, dark, animateFade: false);
        var bmp = new Bitmap(win.ClientSize.Width, win.ClientSize.Height, PixelFormat.Format32bppArgb);
        bmp.SetResolution(96, 96);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        win.PaintSurface(g, bmp.Width, bmp.Height, clipToSquircle: true);
        return bmp;
    }
}
