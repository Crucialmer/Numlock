using System.Drawing.Drawing2D;

namespace Numlock;

/// <summary>通用 GDI+ 几何绘制辅助（图标/菜单/窗口三个模块共用）。</summary>
internal static class Gfx
{
    /// <summary>圆角矩形路径（圆角直径按短边截断）。</summary>
    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>仅右侧圆角的矩形路径（子菜单窗口用：左缘与父面板拼成直边，左角保持直角，右角圆角）。</summary>
    public static GraphicsPath RightRoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddLine(r.X, r.Y, r.Right - d, r.Y);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddLine(r.Right - d, r.Bottom, r.X, r.Bottom);
        path.CloseFigure();
        return path;
    }
}
