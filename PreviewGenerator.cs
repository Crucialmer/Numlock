using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Numlock;

internal static class PreviewGenerator
{
    public static void Run()
    {
        string docsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "docs");
        if (!Directory.Exists(docsDir))
        {
            docsDir = "docs";
            Directory.CreateDirectory(docsDir);
        }

        string? artifactDir = Environment.GetEnvironmentVariable("PREVIEW_ARTIFACT_DIR");

        Console.WriteLine("Generating preview images...");

        // 1. Current Baseline (216px)
        using var rawCurLight = RenderMenu(216, "运行中 · 保持常开已就绪", 18f, 42f, 16f, 16f, 42f, 17.5f, 30f, dark: false);
        using var rawCurDark = RenderMenu(216, "运行中 · 保持常开已就绪", 18f, 42f, 16f, 16f, 42f, 17.5f, 30f, dark: true);
        using var curLight = MakeRoundedMenu(rawCurLight, 8f, dark: false);
        using var curDark = MakeRoundedMenu(rawCurDark, 8f, dark: true);

        // 2. Plan A (188px)
        using var rawPlanALight = RenderMenu(188, "运行中 · 保持常开已就绪", 16f, 40f, 15f, 14f, 40f, 15.5f, 28f, dark: false);
        using var rawPlanADark = RenderMenu(188, "运行中 · 保持常开已就绪", 16f, 40f, 15f, 14f, 40f, 15.5f, 28f, dark: true);
        using var planALight = MakeRoundedMenu(rawPlanALight, 8f, dark: false);
        using var planADark = MakeRoundedMenu(rawPlanADark, 8f, dark: true);

        // 3. Plan B (168px)
        using var rawPlanBLight = RenderMenu(168, "保持常开已激活", 15f, 38f, 14f, 13f, 38f, 14.5f, 27f, dark: false);
        using var rawPlanBDark = RenderMenu(168, "保持常开已激活", 15f, 38f, 14f, 13f, 38f, 14.5f, 27f, dark: true);
        using var planBLight = MakeRoundedMenu(rawPlanBLight, 8f, dark: false);
        using var planBDark = MakeRoundedMenu(rawPlanBDark, 8f, dark: true);

        SaveImage(curLight, Path.Combine(docsDir, "preview-current-light.png"), artifactDir != null ? Path.Combine(artifactDir, "preview-current-light.png") : null);
        SaveImage(curDark, Path.Combine(docsDir, "preview-current-dark.png"), artifactDir != null ? Path.Combine(artifactDir, "preview-current-dark.png") : null);

        SaveImage(planALight, Path.Combine(docsDir, "preview-plan-a-light.png"), artifactDir != null ? Path.Combine(artifactDir, "preview-plan-a-light.png") : null);
        SaveImage(planADark, Path.Combine(docsDir, "preview-plan-a-dark.png"), artifactDir != null ? Path.Combine(artifactDir, "preview-plan-a-dark.png") : null);

        SaveImage(planBLight, Path.Combine(docsDir, "preview-plan-b-light.png"), artifactDir != null ? Path.Combine(artifactDir, "preview-plan-b-light.png") : null);
        SaveImage(planBDark, Path.Combine(docsDir, "preview-plan-b-dark.png"), artifactDir != null ? Path.Combine(artifactDir, "preview-plan-b-dark.png") : null);

        // 4. Generate comparison board
        using var compBoard = RenderComparisonBoard(curLight, curDark, planALight, planADark, planBLight, planBDark);
        SaveImage(compBoard, Path.Combine(docsDir, "preview-comparison.png"), artifactDir != null ? Path.Combine(artifactDir, "preview-comparison.png") : null);

        Console.WriteLine("All previews generated successfully!");
    }

    private static Bitmap MakeRoundedMenu(Bitmap src, float radius, bool dark)
    {
        var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        dst.SetResolution(96, 96);
        using var g = Graphics.FromImage(dst);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Gfx.RoundedRect(new RectangleF(0.5f, 0.5f, src.Width - 1f, src.Height - 1f), radius);
        using var brush = new TextureBrush(src);
        g.FillPath(brush, path);

        Color border = dark ? Color.FromArgb(0x44, 0x44, 0x48) : Color.FromArgb(0xD0, 0xD0, 0xD6);
        using var pen = new Pen(border, 1f);
        g.DrawPath(pen, path);
        return dst;
    }

    private static void SaveImage(Bitmap bmp, string path1, string? path2 = null)
    {
        bmp.Save(path1, ImageFormat.Png);
        Console.WriteLine($"Saved: {path1}");
        if (!string.IsNullOrEmpty(path2) && Directory.Exists(Path.GetDirectoryName(path2)))
        {
            bmp.Save(path2, ImageFormat.Png);
            Console.WriteLine($"Saved: {path2}");
        }
    }

    private static Bitmap RenderMenu(
        int width,
        string statusText,
        float checkX,
        float textX,
        float sepMargin,
        float iconX,
        float titleX,
        float dotX,
        float statusX,
        bool dark)
    {
        var renderer = new ParametricMenuRenderer(dark, textX, checkX, sepMargin);
        var menu = new ParametricContextMenuStrip(width, renderer);
        menu.Font = new Font("Microsoft YaHei", 12f, FontStyle.Bold, GraphicsUnit.Pixel);

        var header = new ParametricBrandHeaderItem(width, statusText, iconX, titleX, dotX, statusX, dark);
        var sep1 = new ToolStripSeparator { Margin = new Padding(0, 4, 0, 4) };
        var toggleItem = new ToolStripMenuItem("关闭 NumLock") { Enabled = false };
        var sep2 = new ToolStripSeparator { Margin = new Padding(0, 4, 0, 4) };
        var enforceItem = new ToolStripMenuItem("保持常开") { Checked = true };
        var autoStartItem = new ToolStripMenuItem("开机自动启动") { Checked = false };
        var colorItem = new ToolStripMenuItem("图标颜色");
        var sep3 = new ToolStripSeparator { Margin = new Padding(0, 4, 0, 4) };
        var updateItem = new ToolStripMenuItem("检查更新…") { Enabled = true };
        var aboutItem = new ToolStripMenuItem("关于软件");
        var exitItem = new ToolStripMenuItem("退出");

        // Dummy item in colorItem dropDown so HasDropDown = true
        colorItem.DropDownItems.Add("自动（跟随系统）");

        var items = new ToolStripItem[]
        {
            header, sep1, toggleItem, sep2, enforceItem, autoStartItem, colorItem, sep3, updateItem, aboutItem, exitItem
        };

        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            if (it is ToolStripSeparator)
            {
                it.Margin = new Padding(0, 0, 0, 0);
            }
            else if (it is not ParametricBrandHeaderItem)
            {
                it.Margin = new Padding(0, 0, 0, i == items.Length - 1 ? 4 : 0);
                it.Padding = new Padding(0, 5, 0, 5);
            }
        }

        menu.Items.AddRange(items);

        menu.Opening += (_, _) =>
        {
            menu.MinimumSize = new Size(width, 0);
            header.Size = new Size(width, 46);
        };

        menu.Show(new Point(300, 300));
        Application.DoEvents();
        Thread.Sleep(80);

        var bmp = new Bitmap(menu.Width, menu.Height);
        bmp.SetResolution(96, 96);
        menu.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        menu.Close();
        menu.Dispose();

        return bmp;
    }

    private static Bitmap RenderComparisonBoard(
        Bitmap curL, Bitmap curD,
        Bitmap planAL, Bitmap planAD,
        Bitmap planBL, Bitmap planBD)
    {
        int boardW = 1580;
        int boardH = 920;
        var board = new Bitmap(boardW, boardH);
        board.SetResolution(96, 96);

        using var g = Graphics.FromImage(board);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Modern studio background: clean neutral dark slate #18191E
        using (var bgBrush = new SolidBrush(Color.FromArgb(0x18, 0x19, 0x1E)))
        {
            g.FillRectangle(bgBrush, 0, 0, boardW, boardH);
        }

        // Top decorative accent glow
        using (var glowBrush = new LinearGradientBrush(
            new Point(0, 0), new Point(boardW, 0),
            Color.FromArgb(90, 0, 122, 255), Color.FromArgb(90, 175, 82, 222)))
        {
            using var glowPen = new Pen(glowBrush, 3.5f);
            g.DrawLine(glowPen, 0, 1, boardW, 1);
        }

        // Title and subtitle
        using (var titleFont = new Font("Microsoft YaHei", 21f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var subFont = new Font("Microsoft YaHei", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            TextRenderer.DrawText(g, "Numlock 托盘菜单紧凑化方案实机视觉对比", titleFont,
                new Point(42, 26), Color.FromArgb(0xF2, 0xF2, 0xF7), TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g, "系统托盘弹出菜单宽度收紧、居中对齐与留白优化实机渲染预览（Light & Dark 模式对比）", subFont,
                new Point(42, 60), Color.FromArgb(0x98, 0x98, 0x9D), TextFormatFlags.NoPadding);
        }

        // 3 Cards layout
        int cardY = 100;
        int cardW = 480;
        int cardH = 780;
        int gap = 24;
        int startX = 42;

        // Card 1: Current Baseline (216px)
        DrawPlanCard(g, startX, cardY, cardW, cardH,
            "当前基准 (v1.5.9)", "216 px 宽屏",
            Color.FromArgb(0x8E, 0x8E, 0x93),
            curL, curD,
            new[]
            {
                ("菜单总宽", "216 px"),
                ("宽高比", "1 : 1.58 (偏宽)"),
                ("对号位置", "X = 18 px"),
                ("文本起始列", "X = 42 px"),
                ("顶栏状态文案", "运行中 · 保持常开已就绪"),
                ("最长项右侧留白", "96 px (占 44%)"),
                ("视觉评价", "右侧空白较多，文字明显偏左")
            });

        // Card 2: Plan A (188px)
        DrawPlanCard(g, startX + cardW + gap, cardY, cardW, cardH,
            "方案 A · 均衡紧凑", "188 px (-28px)",
            Color.FromArgb(0x00, 0x7A, 0xFF),
            planAL, planAD,
            new[]
            {
                ("菜单总宽", "188 px (缩减 13%)"),
                ("宽高比", "1 : 1.81 (稳重协调)"),
                ("对号位置", "X = 16 px (距边框更舒展)"),
                ("文本起始列", "X = 40 px (与图标绝对对齐)"),
                ("顶栏状态文案", "运行中 · 保持常开已就绪 (100%保留)"),
                ("最长项右侧留白", "68 px (比例 40:68)"),
                ("视觉评价", "保留完整文案前提下的黄金比例")
            });

        // Card 3: Plan B (168px)
        DrawPlanCard(g, startX + (cardW + gap) * 2, cardY, cardW, cardH,
            "方案 B · 极致克制", "168 px (-48px)",
            Color.FromArgb(0x34, 0xC7, 0x59),
            planBL, planBD,
            new[]
            {
                ("菜单总宽", "168 px (缩减 22%)"),
                ("宽高比", "1 : 2.02 (macOS 原生比例)"),
                ("对号位置", "X = 15 px"),
                ("文本起始列", "X = 38 px"),
                ("顶栏状态文案", "保持常开已激活 (精炼无冗余)"),
                ("最长项右侧留白", "50 px (接近 1:1.3 黄金对称)"),
                ("视觉评价", "极致修长精致，消除一切多余留白")
            });

        return board;
    }

    private static void DrawPlanCard(
        Graphics g,
        int x, int y, int w, int h,
        string title, string badgeText,
        Color accentColor,
        Bitmap lightImg, Bitmap darkImg,
        (string label, string val)[] specs)
    {
        var cardRect = new RectangleF(x, y, w, h);
        using (var cardPath = Gfx.RoundedRect(cardRect, 14f))
        {
            // Background card
            using var fill = new SolidBrush(Color.FromArgb(0x22, 0x23, 0x2A));
            g.FillPath(fill, cardPath);
            using var stroke = new Pen(Color.FromArgb(0x36, 0x38, 0x42), 1.2f);
            g.DrawPath(stroke, cardPath);
        }

        // Header Title
        using (var tFont = new Font("Microsoft YaHei", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var bFont = new Font("Microsoft YaHei", 11f, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            TextRenderer.DrawText(g, title, tFont, new Point(x + 18, y + 16), Color.White, TextFormatFlags.NoPadding);

            // Badge
            var badgeSize = TextRenderer.MeasureText(badgeText, bFont);
            var badgeRect = new Rectangle(x + w - badgeSize.Width - 28, y + 16, badgeSize.Width + 14, 22);
            using (var bPath = Gfx.RoundedRect(badgeRect, 6f))
            {
                using var bBg = new SolidBrush(Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B));
                g.FillPath(bBg, bPath);
                using var bBorder = new Pen(accentColor, 1f);
                g.DrawPath(bBorder, bPath);
            }
            TextRenderer.DrawText(g, badgeText, bFont,
                new Rectangle(badgeRect.X, badgeRect.Y + 1, badgeRect.Width, badgeRect.Height),
                accentColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // Preview images container (Light + Dark side-by-side)
        int imgAreaY = y + 54;
        int imgAreaH = 410;
        var imgAreaRect = new RectangleF(x + 12, imgAreaY, w - 24, imgAreaH);
        using (var innerPath = Gfx.RoundedRect(imgAreaRect, 10f))
        {
            using var innerFill = new SolidBrush(Color.FromArgb(0x1B, 0x1C, 0x22));
            g.FillPath(innerFill, innerPath);
        }

        // Render light & dark images centered
        int totalMenuW = lightImg.Width + darkImg.Width + 14;
        int startMenuX = (int)(imgAreaRect.X + (imgAreaRect.Width - totalMenuW) / 2f);
        int menuDrawY = imgAreaY + 14;

        // Draw shadow under light menu
        DrawMenuShadow(g, new Rectangle(startMenuX, menuDrawY, lightImg.Width, lightImg.Height));
        g.DrawImage(lightImg, startMenuX, menuDrawY);

        int darkMenuX = startMenuX + lightImg.Width + 14;
        DrawMenuShadow(g, new Rectangle(darkMenuX, menuDrawY, darkImg.Width, darkImg.Height));
        g.DrawImage(darkImg, darkMenuX, menuDrawY);

        // Sublabels for preview
        using (var tagFont = new Font("Microsoft YaHei", 11f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            TextRenderer.DrawText(g, "浅色主题", tagFont,
                new Rectangle(startMenuX, menuDrawY + lightImg.Height + 10, lightImg.Width, 18),
                Color.FromArgb(0x98, 0x98, 0x9D), TextFormatFlags.HorizontalCenter);

            TextRenderer.DrawText(g, "深色主题", tagFont,
                new Rectangle(darkMenuX, menuDrawY + darkImg.Height + 10, darkImg.Width, 18),
                Color.FromArgb(0x98, 0x98, 0x9D), TextFormatFlags.HorizontalCenter);
        }

        // Specs table
        int specY = imgAreaY + imgAreaH + 16;
        using var lblFont = new Font("Microsoft YaHei", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var valFont = new Font("Microsoft YaHei", 12f, FontStyle.Bold, GraphicsUnit.Pixel);

        for (int i = 0; i < specs.Length; i++)
        {
            var (lbl, val) = specs[i];
            int rowY = specY + i * 36;

            // Zebra background row
            if (i % 2 == 1)
            {
                using var rowBg = new SolidBrush(Color.FromArgb(0x1E, 0x1F, 0x26));
                g.FillRectangle(rowBg, x + 12, rowY - 4, w - 24, 30);
            }

            TextRenderer.DrawText(g, lbl, lblFont, new Point(x + 20, rowY),
                Color.FromArgb(0x98, 0x98, 0x9D), TextFormatFlags.NoPadding);

            Color valColor = (lbl == "视觉评价") ? accentColor : Color.FromArgb(0xEE, 0xEE, 0xF2);
            TextRenderer.DrawText(g, val, valFont,
                new Rectangle(x + 110, rowY, w - 132, 26),
                valColor, TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    private static void DrawMenuShadow(Graphics g, Rectangle rect)
    {
        for (int i = 1; i <= 4; i++)
        {
            using var shadowPath = Gfx.RoundedRect(new RectangleF(rect.X - i, rect.Y - i / 2f + 1, rect.Width + i * 2, rect.Height + i * 2), 8f + i);
            using var shadowBrush = new SolidBrush(Color.FromArgb(18 / i, 0, 0, 0));
            g.FillPath(shadowBrush, shadowPath);
        }
    }
}

internal sealed class ParametricContextMenuStrip : ContextMenuStrip
{
    private readonly int _targetWidth;

    public ParametricContextMenuStrip(int targetWidth, ToolStripRenderer renderer)
    {
        _targetWidth = targetWidth;
        Renderer = renderer;
        ShowImageMargin = false;
        ShowCheckMargin = true;
        Padding = new Padding(4, 6, 4, 6);
        MinimumSize = new Size(targetWidth, 0);
    }

    protected override Size MaxItemSize
    {
        get
        {
            float scale = DeviceDpi / 96f;
            int minW = (int)Math.Round(_targetWidth * scale);
            return new Size(Math.Max(base.MaxItemSize.Width, minW), base.MaxItemSize.Height);
        }
    }
}

internal sealed class ParametricBrandHeaderItem : ToolStripMenuItem
{
    private readonly int _targetWidth;
    private readonly string _statusText;
    private readonly float _iconX;
    private readonly float _titleX;
    private readonly float _dotX;
    private readonly float _statusX;
    private readonly bool _dark;

    public ParametricBrandHeaderItem(
        int targetWidth,
        string statusText,
        float iconX,
        float titleX,
        float dotX,
        float statusX,
        bool dark)
    {
        _targetWidth = targetWidth;
        _statusText = statusText;
        _iconX = iconX;
        _titleX = titleX;
        _dotX = dotX;
        _statusX = statusX;
        _dark = dark;

        AutoSize = false;
        Size = new Size(targetWidth, 56);
        Margin = new Padding(0, 6, 0, 0);
    }

    public override Size GetPreferredSize(Size proposedSize) => new Size(_targetWidth, 56);

    public void RenderCard(Graphics g, ToolStrip? toolStrip)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        float padX = 6f;
        float padY = 3f;
        float w = Width - padX * 2f;
        float h = Height - padY * 2f;
        var cardRect = new RectangleF(padX, padY, w, h);
        float corner = 7f;

        Color cardBg = _dark ? Color.FromArgb(0x2E, 0x2E, 0x33) : Color.FromArgb(0xF2, 0xF2, 0xF6);
        Color cardBorder = _dark ? Color.FromArgb(0x44, 0x44, 0x4A) : Color.FromArgb(0xDC, 0xDC, 0xE2);

        using (var path = Gfx.RoundedRect(cardRect, corner))
        {
            using var brush = new SolidBrush(cardBg);
            g.FillPath(brush, path);
            using var pen = new Pen(cardBorder, 1f);
            g.DrawPath(pen, path);
        }

        // Line 1: Icon + Title
        float iconSize = 16f;
        float line1Y = cardRect.Y + 5.5f;
        var iconRect = new RectangleF(_iconX, line1Y + 0.5f, iconSize, iconSize);

        using (var appTile = IconFactory.RenderAppTile((int)Math.Round(iconSize)))
        {
            g.DrawImage(appTile, iconRect);
        }

        using (var brandFont = new Font("Microsoft YaHei", 12f, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            Color brandFg = _dark ? Color.FromArgb(0xF2, 0xF2, 0xF7) : Color.FromArgb(0x1D, 0x1D, 0x1F);
            TextRenderer.DrawText(g, "Numlock", brandFont,
                new Point((int)Math.Round(_titleX), (int)Math.Round(line1Y)),
                brandFg, TextFormatFlags.NoPadding);
        }

        // Line 2: Dot + Status
        float line2Y = cardRect.Y + 23.5f;
        Color dotColor = Color.FromArgb(0x28, 0xC8, 0x40); // Apple Green
        float dotDiameter = 6f;
        float dotY = line2Y + 4f;

        using (var dotBrush = new SolidBrush(dotColor))
            g.FillEllipse(dotBrush, _dotX, dotY, dotDiameter, dotDiameter);

        var statusRect = new Rectangle(
            (int)Math.Round(_statusX),
            (int)Math.Round(line2Y),
            Math.Max(0, (int)Math.Round(cardRect.Right - _statusX - 8f)),
            16);

        using (var statusFont = new Font("Microsoft YaHei", 12f, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            Color statusFg = _dark ? Color.FromArgb(0xF2, 0xF2, 0xF7) : Color.FromArgb(0x1D, 0x1D, 0x1F);
            TextRenderer.DrawText(g, _statusText, statusFont,
                statusRect,
                statusFg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    protected override void OnPaint(PaintEventArgs e) => RenderCard(e.Graphics, Owner);
}

internal sealed class ParametricMenuRenderer : ToolStripRenderer
{
    private readonly bool _dark;
    private readonly float _textX;
    private readonly float _checkX;
    private readonly float _sepMargin;

    public ParametricMenuRenderer(bool dark, float textX, float checkX, float sepMargin)
    {
        _dark = dark;
        _textX = textX;
        _checkX = checkX;
        _sepMargin = sepMargin;
    }

    public Color MenuBack => _dark ? Color.FromArgb(0x2A, 0x2A, 0x2C) : Color.White;
    public Color MenuBorder => _dark ? Color.FromArgb(0x3C, 0x3C, 0x3E) : Color.FromArgb(0xE0, 0xE0, 0xE5);
    public Color MenuText => _dark ? Color.FromArgb(0xF2, 0xF2, 0xF7) : Color.FromArgb(0x1D, 0x1D, 0x1F);
    public Color Dim => _dark ? Color.FromArgb(0x98, 0x98, 0x9D) : Color.FromArgb(0x8E, 0x8E, 0x93);
    public Color Accent => _dark ? Color.FromArgb(0x0A, 0x84, 0xFF) : Color.FromArgb(0x00, 0x7A, 0xFF);

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(MenuBack);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.ToolStrip.Size));
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(MenuBorder, 1f);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is ParametricBrandHeaderItem header)
        {
            header.RenderCard(e.Graphics, e.ToolStrip);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is ParametricBrandHeaderItem)
            return;

        var textRect = new Rectangle(
            (int)Math.Round(_textX),
            0,
            Math.Max(0, (int)Math.Round(e.Item.Width - _textX - 12f)),
            e.Item.Height);

        Color textColor = !e.Item.Enabled ? Dim : MenuText;

        TextRenderer.DrawText(
            e.Graphics,
            e.Text,
            e.TextFont,
            textRect,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item is ParametricBrandHeaderItem)
            return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float checkW = 12f;
        float checkH = 10f;
        float x = _checkX;
        float y = (e.Item.Height - checkH) / 2f;

        using var pen = new Pen(Accent, 2.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        var pts = new[]
        {
            new PointF(x, y + checkH * 0.48f),
            new PointF(x + checkW * 0.38f, y + checkH),
            new PointF(x + checkW, y + 1f),
        };
        g.DrawLines(pen, pts);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        e.Graphics.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(MenuBorder, 1f);
        e.Graphics.DrawLine(pen, _sepMargin, y, e.Item.Width - _sepMargin, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Item is not ToolStripMenuItem { HasDropDown: true })
            return;

        float w = 4.5f;
        float h = 8.5f;
        float cx = e.Item.Width - 16f - w / 2f;
        float cy = e.Item.Size.Height / 2f;
        using var pen = new Pen(Dim, 1.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, new[]
        {
            new PointF(cx - w / 2f, cy - h / 2f),
            new PointF(cx + w / 2f, cy),
            new PointF(cx - w / 2f, cy + h / 2f),
        });
    }
}
