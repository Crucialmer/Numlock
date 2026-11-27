using System.Drawing.Drawing2D;

namespace Numlock;

/// <summary>
/// macOS 26 (Tahoe) Liquid Glass 模态对话框：替代原生 MessageBox，提供统一的液态玻璃视觉体验。
/// · 光学玻璃渐变底色 + 顶缘高光反射 + DWM 瞬态亚克力材质
/// · 全胶囊交互按钮（Full Pill Capsule）+ Liquid Glass 矢量图标 + 无闪烁原生拖拽
/// · 全 DPI 适配（100%–400% / 8K），自绘异常时自动降级到系统原生 MessageBox
/// </summary>
internal sealed class MacDialog : Form
{
    internal enum DialogIcon { Info, Warning, Error }
    internal enum DialogButtons { Ok, OkCancel, YesNo }

    private readonly string _title;
    private readonly string _message;
    private readonly DialogIcon _icon;
    private readonly DialogButtons _buttons;
    private readonly bool _dark;
    private readonly bool _hasSecondary;

    private float _scale;
    private bool _dwmRounded;
    private int _iconSize;
    private int _btnHeight;
    private int _btnMinWidth;
    private int _padding;

    private bool _primaryHover;
    private bool _primaryPressed;
    private bool _secondaryHover;
    private bool _secondaryPressed;
    private RectangleF _primaryBtnRect;
    private RectangleF _secondaryBtnRect;

    private Font? _titleFont;
    private Font? _bodyFont;
    private Font? _btnBoldFont;
    private Font? _btnRegFont;

    private LiquidGlass.Palette Pal => LiquidGlass.Get(_dark);

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
        _iconSize = (int)Math.Round(44 * _scale);
        _btnHeight = (int)Math.Round(32 * _scale);
        _btnMinWidth = (int)Math.Round(82 * _scale);
        _padding = (int)Math.Round(24 * _scale);

        DisposeFonts();
        _titleFont = LiquidGlass.CreateFont(17f * _scale, FontStyle.Bold);
        _bodyFont = LiquidGlass.CreateFont(13.5f * _scale, FontStyle.Regular);
        _btnBoldFont = LiquidGlass.CreateFont(13.5f * _scale, FontStyle.Bold);
        _btnRegFont = LiquidGlass.CreateFont(13.5f * _scale, FontStyle.Regular);
    }

    private void DisposeFonts()
    {
        _titleFont?.Dispose();
        _bodyFont?.Dispose();
        _btnBoldFont?.Dispose();
        _btnRegFont?.Dispose();
        _titleFont = _bodyFont = _btnBoldFont = _btnRegFont = null;
    }

    private void UpdateWindowRegion()
    {
        _dwmRounded = LiquidGlass.ConfigureGlassWindow(this, _dark, 14f * _scale);
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
        BackColor = Pal.WindowSolidFallback;
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;

        int dialogWidth = (int)Math.Round(380 * _scale);
        int contentWidth = dialogWidth - _padding * 2;

        int titleHeight;
        int bodyHeight;
        using (var g = CreateGraphics())
        {
            titleHeight = (int)Math.Ceiling(g.MeasureString(_title, _titleFont!, contentWidth).Height);
            bodyHeight = (int)Math.Ceiling(g.MeasureString(_message, _bodyFont!, contentWidth).Height);
        }

        int totalHeight = _padding
                        + _iconSize
                        + (int)Math.Round(12 * _scale)
                        + titleHeight
                        + (int)Math.Round(8 * _scale)
                        + bodyHeight
                        + (int)Math.Round(20 * _scale)
                        + 1
                        + (int)Math.Round(16 * _scale)
                        + _btnHeight
                        + _padding;

        ClientSize = new Size(dialogWidth, totalHeight);
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
        LiquidGlass.ConfigureGraphics(g);
        var p = Pal;

        int dialogWidth = ClientSize.Width;
        int dialogHeight = ClientSize.Height;
        int contentWidth = dialogWidth - _padding * 2;

        LiquidGlass.FillWindowSurface(g, new RectangleF(0, 0, dialogWidth, dialogHeight), p);

        int y = _padding;

        // 1. Liquid Glass 状态图标
        int iconX = (dialogWidth - _iconSize) / 2;
        DrawMacIcon(g, iconX, y, _iconSize);
        y += _iconSize + (int)Math.Round(12 * _scale);

        // 2. 标题
        var titleSize = g.MeasureString(_title, _titleFont!, contentWidth);
        LiquidGlass.DrawTextCenter(g, _title, _titleFont!, p.TextPrimary, new RectangleF(_padding, y, contentWidth, titleSize.Height), wordBreak: true);
        y += (int)Math.Ceiling(titleSize.Height) + (int)Math.Round(8 * _scale);

        // 3. 正文
        var bodySize = g.MeasureString(_message, _bodyFont!, contentWidth);
        LiquidGlass.DrawTextCenter(g, _message, _bodyFont!, p.TextSecondary, new RectangleF(_padding, y, contentWidth, bodySize.Height), wordBreak: true);
        y += (int)Math.Ceiling(bodySize.Height) + (int)Math.Round(20 * _scale);

        // 4. 发丝分隔线
        using (var sepPen = new Pen(p.Separator, 1f))
        {
            g.DrawLine(sepPen, _padding, y, dialogWidth - _padding, y);
        }
        y += 1 + (int)Math.Round(16 * _scale);

        // 5. 全胶囊操作按钮
        DrawButtons(g, y, dialogWidth);

        // 6. 顶缘高光玻璃描边
        float borderRadius = _dwmRounded ? 8f * _scale : 14f * _scale;
        var rimRect = new RectangleF(0.5f, 0.5f, dialogWidth - 1f, dialogHeight - 1f);
        using var borderPath = Gfx.RoundedRect(rimRect, Math.Max(1f, borderRadius - 0.5f));
        LiquidGlass.DrawGlassRim(g, borderPath, rimRect, p, 1f);
    }

    private void DrawButtons(Graphics g, int y, int dialogWidth)
    {
        var p = Pal;
        string primaryText = _buttons == DialogButtons.YesNo ? "是" : "好";
        string secondaryText = _buttons switch
        {
            DialogButtons.YesNo => "否",
            DialogButtons.OkCancel => "取消",
            _ => "",
        };

        int spacing = (int)Math.Round(10 * _scale);
        int primaryW = Math.Max(_btnMinWidth, (int)Math.Ceiling(g.MeasureString(primaryText, _btnBoldFont!).Width) + (int)Math.Round(30 * _scale));

        if (_hasSecondary)
        {
            int secondaryW = Math.Max(_btnMinWidth, (int)Math.Ceiling(g.MeasureString(secondaryText, _btnRegFont!).Width) + (int)Math.Round(30 * _scale));
            int totalW = primaryW + spacing + secondaryW;
            int startX = (dialogWidth - totalW) / 2;

            _secondaryBtnRect = new RectangleF(startX, y, secondaryW, _btnHeight);
            LiquidGlass.DrawPillButton(g, _secondaryBtnRect, secondaryText, _btnRegFont!, p, isPrimary: false, _secondaryHover, _secondaryPressed);

            _primaryBtnRect = new RectangleF(startX + secondaryW + spacing, y, primaryW, _btnHeight);
            LiquidGlass.DrawPillButton(g, _primaryBtnRect, primaryText, _btnBoldFont!, p, isPrimary: true, _primaryHover, _primaryPressed);
        }
        else
        {
            int startX = (dialogWidth - primaryW) / 2;
            _primaryBtnRect = new RectangleF(startX, y, primaryW, _btnHeight);
            LiquidGlass.DrawPillButton(g, _primaryBtnRect, primaryText, _btnBoldFont!, p, isPrimary: true, _primaryHover, _primaryPressed);
        }
    }

    private void DrawMacIcon(Graphics g, int x, int y, int size)
    {
        var p = Pal;
        float cx = x + size / 2f;
        float cy = y + size / 2f;
        float r = size / 2f;

        switch (_icon)
        {
            case DialogIcon.Info:
                LiquidGlass.DrawGlassOrb(g, x, y, size, p.AccentSolid);
                float infoPenW = Math.Max(2f, r * 0.14f);
                using (var pen = new Pen(Color.White, infoPenW) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var dotBrush = new SolidBrush(Color.White))
                {
                    float dotR = infoPenW * 0.85f;
                    g.FillEllipse(dotBrush, cx - dotR, cy - r * 0.44f - dotR, dotR * 2f, dotR * 2f);
                    g.DrawLine(pen, cx, cy - r * 0.14f, cx, cy + r * 0.46f);
                }
                break;

            case DialogIcon.Warning:
                LiquidGlass.DrawGlassOrb(g, x, y, size, p.WarningAmber);
                float warnPenW = Math.Max(2.2f, r * 0.14f);
                using (var pen = new Pen(Color.White, warnPenW) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var dotBrush = new SolidBrush(Color.White))
                {
                    g.DrawLine(pen, cx, cy - r * 0.36f, cx, cy + r * 0.12f);
                    float dotR = warnPenW * 0.78f;
                    g.FillEllipse(dotBrush, cx - dotR, cy + r * 0.38f - dotR, dotR * 2f, dotR * 2f);
                }
                break;

            case DialogIcon.Error:
                LiquidGlass.DrawGlassOrb(g, x, y, size, p.ErrorRed);
                float errPenW = Math.Max(2.2f, r * 0.16f);
                float crossR = r * 0.34f;
                using (var pen = new Pen(Color.White, errPenW) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, cx - crossR, cy - crossR, cx + crossR, cy + crossR);
                    g.DrawLine(pen, cx + crossR, cy - crossR, cx - crossR, cy + crossR);
                }
                break;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (_primaryBtnRect.Contains(e.Location))
            {
                _primaryPressed = true;
                Invalidate();
                return;
            }
            if (_hasSecondary && _secondaryBtnRect.Contains(e.Location))
            {
                _secondaryPressed = true;
                Invalidate();
                return;
            }
            LiquidGlass.BeginSmoothDrag(Handle);
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
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeFonts();
        }
        base.Dispose(disposing);
    }

    public static DialogResult ShowInfo(string title, string message)
        => ShowDialog(title, message, DialogIcon.Info, DialogButtons.Ok);

    public static DialogResult ShowWarning(string title, string message)
        => ShowDialog(title, message, DialogIcon.Warning, DialogButtons.Ok);

    public static DialogResult ShowWarningOkCancel(string title, string message)
        => ShowDialog(title, message, DialogIcon.Warning, DialogButtons.OkCancel);

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
}
