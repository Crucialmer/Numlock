using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Numlock;

/// <summary>
/// macOS 26 (Tahoe) Liquid Glass 设计美学核心引擎：
/// · 统一浅/深双模玻璃材质色彩令牌、光学渐变底色、顶缘高光反射（Specular Highlight）与底缘折射边界
/// · 同心连续曲率圆角（Concentric Squircle）与全胶囊交互按钮（Full Pill Capsule）
/// · 共享矢量组件（交通灯关闭按钮、Liquid Glass 状态徽标、内嵌玻璃卡片、12 辐条流体加载指示器）
/// · 资源缓存（AppTile 位图缓存、无 GC 分配的复用 StringFormat、安全计时器释放、统一 DWM 玻璃窗口配置）
/// </summary>
internal static class LiquidGlass
{
    internal readonly record struct Palette(
        bool Dark,
        Color WindowBgTop,
        Color WindowBgBottom,
        Color WindowSolidFallback,
        Color MenuBgTop,
        Color MenuBgBottom,
        Color CardBgTop,
        Color CardBgBottom,
        Color CardHoverTop,
        Color CardHoverBottom,
        Color SpecularTop,
        Color SpecularMid,
        Color SpecularBottom,
        Color OuterStroke,
        Color Separator,
        Color TextPrimary,
        Color TextSecondary,
        Color TextMuted,
        Color AccentTop,
        Color AccentBottom,
        Color AccentHoverTop,
        Color AccentHoverBottom,
        Color AccentPressedTop,
        Color AccentPressedBottom,
        Color AccentSolid,
        Color SecondaryBtnTop,
        Color SecondaryBtnBottom,
        Color SecondaryBtnHoverTop,
        Color SecondaryBtnHoverBottom,
        Color SecondaryBtnPressedTop,
        Color SecondaryBtnPressedBottom,
        Color SuccessGreen,
        Color WarningAmber,
        Color ErrorRed);

    public static readonly Palette DarkPalette = new(
        Dark: true,
        WindowBgTop: Color.FromArgb(255, 38, 39, 44),
        WindowBgBottom: Color.FromArgb(255, 27, 28, 33),
        WindowSolidFallback: Color.FromArgb(255, 32, 33, 38),
        MenuBgTop: Color.FromArgb(255, 42, 43, 48),
        MenuBgBottom: Color.FromArgb(255, 33, 34, 39),
        CardBgTop: Color.FromArgb(30, 255, 255, 255),
        CardBgBottom: Color.FromArgb(14, 255, 255, 255),
        CardHoverTop: Color.FromArgb(46, 255, 255, 255),
        CardHoverBottom: Color.FromArgb(24, 255, 255, 255),
        SpecularTop: Color.FromArgb(92, 255, 255, 255),
        SpecularMid: Color.FromArgb(30, 255, 255, 255),
        SpecularBottom: Color.FromArgb(18, 255, 255, 255),
        OuterStroke: Color.FromArgb(52, 255, 255, 255),
        Separator: Color.FromArgb(34, 255, 255, 255),
        TextPrimary: Color.FromArgb(255, 245, 246, 250),
        TextSecondary: Color.FromArgb(182, 235, 238, 245),
        TextMuted: Color.FromArgb(255, 146, 148, 158),
        AccentTop: Color.FromArgb(255, 28, 148, 255),
        AccentBottom: Color.FromArgb(255, 8, 118, 245),
        AccentHoverTop: Color.FromArgb(255, 48, 162, 255),
        AccentHoverBottom: Color.FromArgb(255, 18, 132, 255),
        AccentPressedTop: Color.FromArgb(255, 8, 114, 230),
        AccentPressedBottom: Color.FromArgb(255, 4, 96, 206),
        AccentSolid: Color.FromArgb(255, 10, 132, 255),
        SecondaryBtnTop: Color.FromArgb(46, 255, 255, 255),
        SecondaryBtnBottom: Color.FromArgb(28, 255, 255, 255),
        SecondaryBtnHoverTop: Color.FromArgb(64, 255, 255, 255),
        SecondaryBtnHoverBottom: Color.FromArgb(42, 255, 255, 255),
        SecondaryBtnPressedTop: Color.FromArgb(34, 255, 255, 255),
        SecondaryBtnPressedBottom: Color.FromArgb(22, 255, 255, 255),
        SuccessGreen: Color.FromArgb(255, 48, 209, 88),
        WarningAmber: Color.FromArgb(255, 255, 179, 0),
        ErrorRed: Color.FromArgb(255, 255, 69, 58));

    public static readonly Palette LightPalette = new(
        Dark: false,
        WindowBgTop: Color.FromArgb(255, 250, 251, 253),
        WindowBgBottom: Color.FromArgb(255, 236, 238, 244),
        WindowSolidFallback: Color.FromArgb(255, 244, 245, 249),
        MenuBgTop: Color.FromArgb(255, 254, 254, 255),
        MenuBgBottom: Color.FromArgb(255, 243, 245, 249),
        CardBgTop: Color.FromArgb(220, 255, 255, 255),
        CardBgBottom: Color.FromArgb(145, 244, 246, 250),
        CardHoverTop: Color.FromArgb(245, 255, 255, 255),
        CardHoverBottom: Color.FromArgb(190, 236, 240, 248),
        SpecularTop: Color.FromArgb(250, 255, 255, 255),
        SpecularMid: Color.FromArgb(34, 0, 0, 0),
        SpecularBottom: Color.FromArgb(44, 0, 0, 0),
        OuterStroke: Color.FromArgb(36, 0, 0, 0),
        Separator: Color.FromArgb(24, 0, 0, 0),
        TextPrimary: Color.FromArgb(255, 24, 25, 28),
        TextSecondary: Color.FromArgb(165, 24, 25, 28),
        TextMuted: Color.FromArgb(255, 140, 142, 150),
        AccentTop: Color.FromArgb(255, 24, 140, 255),
        AccentBottom: Color.FromArgb(255, 0, 112, 245),
        AccentHoverTop: Color.FromArgb(255, 42, 152, 255),
        AccentHoverBottom: Color.FromArgb(255, 10, 126, 255),
        AccentPressedTop: Color.FromArgb(255, 0, 104, 224),
        AccentPressedBottom: Color.FromArgb(255, 0, 88, 198),
        AccentSolid: Color.FromArgb(255, 0, 122, 255),
        SecondaryBtnTop: Color.FromArgb(230, 255, 255, 255),
        SecondaryBtnBottom: Color.FromArgb(185, 232, 234, 240),
        SecondaryBtnHoverTop: Color.FromArgb(250, 255, 255, 255),
        SecondaryBtnHoverBottom: Color.FromArgb(210, 222, 226, 236),
        SecondaryBtnPressedTop: Color.FromArgb(195, 222, 225, 232),
        SecondaryBtnPressedBottom: Color.FromArgb(175, 210, 214, 224),
        SuccessGreen: Color.FromArgb(255, 40, 200, 74),
        WarningAmber: Color.FromArgb(255, 245, 158, 11),
        ErrorRed: Color.FromArgb(255, 255, 59, 48));

    public static Palette Get(bool dark) => dark ? DarkPalette : LightPalette;

    // 复用只读 StringFormat，避免每帧 OnPaint 分配 GDI+ 句柄
    private static readonly StringFormat SfCenterNoWrap = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    private static readonly StringFormat SfCenterWrap = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
    };

    private static readonly StringFormat SfLeftNoWrap = new()
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    private static readonly StringFormat SfLeftWrap = new()
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
    };

    // 应用磁贴位图缓存（避免 25 FPS Spinner 动画或悬停重绘期间反复光栅化）
    private static readonly object TileCacheLock = new();
    private static readonly Dictionary<int, Bitmap> TileCache = new();
    private static readonly Dictionary<(int Size, bool NumOn, bool White, bool Badge), Bitmap> TrayCache = new();

    public static Bitmap GetCachedAppTile(int size)
    {
        size = Math.Clamp(size, 8, 512);
        lock (TileCacheLock)
        {
            if (TileCache.TryGetValue(size, out var cached))
            {
                return cached;
            }
            var bmp = IconFactory.RenderAppTile(size);
            TileCache[size] = bmp;
            return bmp;
        }
    }

    public static Bitmap GetCachedTrayIcon(int size, bool numOn, bool whiteIcons, bool enforceBadge)
    {
        size = Math.Clamp(size, 8, 256);
        var key = (size, numOn, whiteIcons, enforceBadge);
        lock (TileCacheLock)
        {
            if (TrayCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
            var bmp = IconFactory.RenderTrayIcon(size, numOn, whiteIcons, enforceBadge);
            TrayCache[key] = bmp;
            return bmp;
        }
    }

    /// <summary>绘制缓存磁贴（加锁保护，防止多线程读取同一 Bitmap 冲突）。</summary>
    public static void DrawCachedAppTile(Graphics g, RectangleF dest, int renderSize)
    {
        lock (TileCacheLock)
        {
            var bmp = GetCachedAppTile(renderSize);
            g.DrawImage(bmp, dest);
        }
    }

    public static void DrawCachedTrayIcon(Graphics g, RectangleF dest, int renderSize, bool numOn, bool whiteIcons, bool enforceBadge)
    {
        lock (TileCacheLock)
        {
            var bmp = GetCachedTrayIcon(renderSize, numOn, whiteIcons, enforceBadge);
            g.DrawImage(bmp, dest);
        }
    }

    /// <summary>创建跨系统清晰一致的 UI 字体（优先 Microsoft YaHei UI）。</summary>
    public static Font CreateFont(float pixelSize, FontStyle style = FontStyle.Regular)
    {
        float sz = Math.Max(6f, pixelSize);
        try
        {
            return new Font("Microsoft YaHei UI", sz, style, GraphicsUnit.Pixel);
        }
        catch
        {
            try
            {
                return new Font("Microsoft YaHei", sz, style, GraphicsUnit.Pixel);
            }
            catch
            {
                return new Font(FontFamily.GenericSansSerif, sz, style, GraphicsUnit.Pixel);
            }
        }
    }

    /// <summary>配置高质量抗锯齿画布（杜绝 ClearType 在半透明玻璃底色上的彩色边缘重影）。</summary>
    public static void ConfigureGraphics(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    /// <summary>填充 macOS 26 Liquid Glass 面板光学底色（含柔和顶部光晕）。</summary>
    public static void FillWindowSurface(Graphics g, RectangleF bounds, Palette p, GraphicsPath? clipPath = null)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var bgBrush = new LinearGradientBrush(
            new PointF(bounds.X, bounds.Y),
            new PointF(bounds.X, bounds.Bottom),
            p.WindowBgTop,
            p.WindowBgBottom)
        {
            WrapMode = WrapMode.TileFlipXY,
        };

        if (clipPath != null)
        {
            g.FillPath(bgBrush, clipPath);
        }
        else
        {
            g.FillRectangle(bgBrush, bounds);
        }

        // 顶部柔和环境高光（自然衰减至面板中部，无断层边界）
        float sheenH = bounds.Height * 0.48f;
        var sheenRect = new RectangleF(bounds.X, bounds.Y, bounds.Width, sheenH);
        using var sheenBrush = new LinearGradientBrush(
            new PointF(sheenRect.X, sheenRect.Y),
            new PointF(sheenRect.X, sheenRect.Bottom),
            Color.FromArgb(p.Dark ? 20 : 115, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255))
        {
            WrapMode = WrapMode.TileFlipXY,
        };

        if (clipPath != null)
        {
            var state = g.Save();
            g.SetClip(clipPath, CombineMode.Intersect);
            g.FillRectangle(sheenBrush, sheenRect);
            g.Restore(state);
        }
        else
        {
            g.FillRectangle(sheenBrush, sheenRect);
        }
    }

    /// <summary>绘制 macOS 26 Liquid Glass 光学边缘描边（顶部高光反射 + 底部折射收边）。</summary>
    public static void DrawGlassRim(Graphics g, GraphicsPath path, RectangleF bounds, Palette p, float width = 1f)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var rimBrush = new LinearGradientBrush(
            new PointF(bounds.X, bounds.Y),
            new PointF(bounds.X, bounds.Bottom),
            p.SpecularTop,
            p.SpecularBottom)
        {
            WrapMode = WrapMode.TileFlipXY,
        };

        var blend = new ColorBlend(3)
        {
            Colors = [p.SpecularTop, p.SpecularMid, p.SpecularBottom],
            Positions = [0f, 0.35f, 1f],
        };
        rimBrush.InterpolationColors = blend;

        using var pen = new Pen(rimBrush, width);
        g.DrawPath(pen, path);
    }

    /// <summary>绘制 macOS 26 Liquid Glass 内嵌卡片（磨砂光学填充 + 顶部高光边缘）。</summary>
    public static void DrawInsetCard(Graphics g, RectangleF rect, float radius, Palette p, bool hover = false, float strokeWidth = 1f)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using var path = Gfx.RoundedRect(rect, radius);
        using (var fill = new LinearGradientBrush(
            new PointF(rect.X, rect.Y),
            new PointF(rect.X, rect.Bottom),
            hover ? p.CardHoverTop : p.CardBgTop,
            hover ? p.CardHoverBottom : p.CardBgBottom)
        {
            WrapMode = WrapMode.TileFlipXY,
        })
        {
            g.FillPath(fill, path);
        }

        DrawGlassRim(g, path, rect, p, strokeWidth);
    }

    /// <summary>绘制 macOS 26 全胶囊交互按钮（Full Pill Capsule + 顶缘液态高光折射）。</summary>
    public static void DrawPillButton(
        Graphics g,
        RectangleF rect,
        string text,
        Font font,
        Palette p,
        bool isPrimary,
        bool hover,
        bool pressed)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        float radius = rect.Height / 2f;
        using var path = Gfx.RoundedRect(rect, radius);

        Color topColor, bottomColor, textColor;
        if (isPrimary)
        {
            topColor = pressed ? p.AccentPressedTop : (hover ? p.AccentHoverTop : p.AccentTop);
            bottomColor = pressed ? p.AccentPressedBottom : (hover ? p.AccentHoverBottom : p.AccentBottom);
            textColor = Color.White;
        }
        else
        {
            topColor = pressed ? p.SecondaryBtnPressedTop : (hover ? p.SecondaryBtnHoverTop : p.SecondaryBtnTop);
            bottomColor = pressed ? p.SecondaryBtnPressedBottom : (hover ? p.SecondaryBtnHoverBottom : p.SecondaryBtnBottom);
            textColor = p.TextPrimary;
        }

        using (var fill = new LinearGradientBrush(
            new PointF(rect.X, rect.Y),
            new PointF(rect.X, rect.Bottom),
            topColor,
            bottomColor))
        {
            g.FillPath(fill, path);
        }

        // 顶部光学镜面微光边（Liquid Glass 标志性立体折射质感）
        var insetRect = new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1f, rect.Height - 1f);
        using (var rimPath = Gfx.RoundedRect(insetRect, Math.Max(1f, radius - 0.5f)))
        {
            Color rimTop = isPrimary
                ? Color.FromArgb(pressed ? 40 : 120, 255, 255, 255)
                : (p.Dark ? Color.FromArgb(75, 255, 255, 255) : Color.FromArgb(240, 255, 255, 255));
            Color rimBottom = isPrimary
                ? Color.FromArgb(24, 0, 0, 0)
                : (p.Dark ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(34, 0, 0, 0));

            using var rimBrush = new LinearGradientBrush(
                new PointF(insetRect.X, insetRect.Y),
                new PointF(insetRect.X, insetRect.Bottom),
                rimTop,
                rimBottom);
            using var rimPen = new Pen(rimBrush, 1f);
            g.DrawPath(rimPen, rimPath);
        }

        DrawTextCenter(g, text, font, textColor, rect);
    }

    /// <summary>绘制 macOS 26 磨砂玻璃版本胶囊徽标。</summary>
    public static void DrawVersionBadge(Graphics g, RectangleF rect, string text, Font font, Palette p)
    {
        float radius = rect.Height / 2f;
        using var path = Gfx.RoundedRect(rect, radius);

        using (var fill = new LinearGradientBrush(
            new PointF(rect.X, rect.Y),
            new PointF(rect.X, rect.Bottom),
            Color.FromArgb(p.Dark ? 52 : 34, p.AccentSolid),
            Color.FromArgb(p.Dark ? 28 : 18, p.AccentSolid)))
        {
            g.FillPath(fill, path);
        }

        using (var rimBrush = new LinearGradientBrush(
            new PointF(rect.X, rect.Y),
            new PointF(rect.X, rect.Bottom),
            Color.FromArgb(p.Dark ? 110 : 75, p.AccentSolid),
            Color.FromArgb(p.Dark ? 45 : 32, p.AccentSolid)))
        using (var pen = new Pen(rimBrush, 1f))
        {
            g.DrawPath(pen, path);
        }

        DrawTextCenter(g, text, font, p.AccentSolid, rect);
    }

    /// <summary>绘制 macOS 经典交通灯红点关闭按钮（带细腻外圈光泽与悬停叉号）。</summary>
    public static void DrawTrafficClose(Graphics g, float x, float y, float size, float scale, bool hover, bool pressed)
    {
        Color top = pressed
            ? Color.FromArgb(255, 214, 58, 50)
            : (hover ? Color.FromArgb(255, 255, 118, 110) : Color.FromArgb(255, 255, 102, 94));
        Color bottom = pressed
            ? Color.FromArgb(255, 192, 44, 36)
            : (hover ? Color.FromArgb(255, 246, 86, 78) : Color.FromArgb(255, 238, 76, 68));

        var rect = new RectangleF(x, y, size, size);
        using (var fill = new LinearGradientBrush(new PointF(x, y), new PointF(x, y + size), top, bottom))
        {
            g.FillEllipse(fill, rect);
        }
        using (var ring = new Pen(Color.FromArgb(46, 0, 0, 0), Math.Max(0.75f, 0.8f * scale)))
        {
            g.DrawEllipse(ring, rect);
        }

        if (hover)
        {
            using var xPen = new Pen(Color.FromArgb(195, 76, 4, 2), Math.Max(1.1f, 1.3f * scale))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            float cx = x + size / 2f;
            float cy = y + size / 2f;
            float off = size * 0.21f;
            g.DrawLine(xPen, cx - off, cy - off, cx + off, cy + off);
            g.DrawLine(xPen, cx + off, cy - off, cx - off, cy + off);
        }
    }

    /// <summary>绘制 macOS 26 Liquid Glass 光学圆球底托（含顶部高光折射，供各类状态图标复用）。</summary>
    public static void DrawGlassOrb(Graphics g, float x, float y, float size, Color baseColor)
    {
        var rect = new RectangleF(x, y, size, size);
        Color lighter = Blend(baseColor, Color.White, 0.22f);
        Color deeper = Blend(baseColor, Color.Black, 0.12f);

        using (var fill = new LinearGradientBrush(new PointF(x, y), new PointF(x, y + size), lighter, deeper))
        {
            g.FillEllipse(fill, rect);
        }

        // 顶部内高光折射环
        var inset = new RectangleF(x + 0.75f, y + 0.75f, size - 1.5f, size - 1.5f);
        using var rimBrush = new LinearGradientBrush(
            new PointF(inset.X, inset.Y),
            new PointF(inset.X, inset.Bottom),
            Color.FromArgb(135, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255));
        using var rimPen = new Pen(rimBrush, 1f);
        g.DrawEllipse(rimPen, inset);
    }

    /// <summary>绘制 macOS 原生 12 辐条旋转加载指示器。</summary>
    public static void DrawMacSpinner(Graphics g, float cx, float cy, float radius, float scale, Color fgColor, int step)
    {
        float innerR = radius * 0.48f;
        float outerR = radius;
        float stroke = Math.Max(1.6f, 2.2f * scale);
        for (int i = 0; i < 12; i++)
        {
            int diff = (i - step + 12) % 12;
            int alpha = (int)(38 + 217 * (1.0f - diff / 12.0f));
            Color spokeColor = Color.FromArgb(alpha, fgColor);

            double angle = i * Math.PI / 6.0;
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);

            using var pen = new Pen(spokeColor, stroke)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawLine(pen, cx + innerR * cos, cy + innerR * sin, cx + outerR * cos, cy + outerR * sin);
        }
    }

    /// <summary>精确量测紧凑文本宽度（避免默认 GDI+ MeasureString 右侧多余留白导致徽标视觉偏左）。</summary>
    public static SizeF MeasureTightText(Graphics g, string text, Font font) =>
        g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);

    public static void DrawTextCenter(Graphics g, string text, Font font, Color color, RectangleF rect, bool wordBreak = false)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, rect, wordBreak ? SfCenterWrap : SfCenterNoWrap);
    }

    public static void DrawTextLeft(Graphics g, string text, Font font, Color color, RectangleF rect, bool wordBreak = false)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, rect, wordBreak ? SfLeftWrap : SfLeftNoWrap);
    }

    public static Color Blend(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(a.A + (b.A - a.A) * t),
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>统一配置窗口的 Win11 DWM 圆角、深色帧、瞬态亚克力材质与和谐边框色，并安全处理回退 Region。</summary>
    public static bool ConfigureGlassWindow(Form form, bool dark, float fallbackCornerRadius)
    {
        bool rounded = false;
        try
        {
            rounded = Native.TryRoundCorners(form.Handle);
            Native.SetImmersiveDarkMode(form.Handle, dark);
            Native.TrySetTransientBackdrop(form.Handle);
            if (rounded)
            {
                // 与液态玻璃外缘协调的细腻 DWM 边框，避免 Win11 默认灰框破坏观感
                Color dwmBorder = dark ? Color.FromArgb(0x46, 0x48, 0x52) : Color.FromArgb(0xCF, 0xD2, 0xDC);
                Native.TrySetBorderColor(form.Handle, dwmBorder);
                var oldRegion = form.Region;
                form.Region = null;
                oldRegion?.Dispose();
            }
            else
            {
                using var path = Gfx.RoundedRect(new RectangleF(0, 0, form.Width, form.Height), fallbackCornerRadius);
                var oldRegion = form.Region;
                form.Region = new Region(path);
                oldRegion?.Dispose();
            }
        }
        catch
        {
        }
        return rounded;
    }

    /// <summary>触发 Win32 原生无闪烁平滑窗口拖拽（支持多屏 DPI 漫游）。</summary>
    public static void BeginSmoothDrag(IntPtr handle)
    {
        try
        {
            Native.ReleaseCapture();
            Native.SendMessage(handle, 0xA1, 2, 0); // WM_NCLBUTTONDOWN, HTCAPTION
        }
        catch
        {
        }
    }

    /// <summary>安全停止并销毁 WinForms Timer，消除重复释放与竞态风险。</summary>
    public static void DisposeTimer(ref System.Windows.Forms.Timer? timer)
    {
        var t = timer;
        timer = null;
        if (t != null)
        {
            try
            {
                t.Stop();
                t.Dispose();
            }
            catch
            {
            }
        }
    }
}
