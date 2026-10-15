using System.Drawing.Drawing2D;

namespace Numlock;

/// <summary>通用 GDI+ 几何绘制辅助（图标/菜单/窗口共用），支持 macOS 风格连续曲率（G2 Squircle）。</summary>
internal static class Gfx
{
    /// <summary>
    /// 圆角矩形路径：采用连续曲率平滑过渡（Squircle），消除普通圆弧切点处的曲率断裂折痕，
    /// 符合 macOS 26 的流体温润设计美学。
    /// </summary>
    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (r.Width <= 0 || r.Height <= 0)
        {
            return path;
        }

        float maxR = Math.Min(r.Width, r.Height) / 2f;
        float actualR = Math.Min(radius, maxR);
        if (actualR <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }

        AddContinuousCorners(path, r.X, r.Y, r.Width, r.Height, actualR);
        path.CloseFigure();
        return path;
    }

    /// <summary>仅右侧圆角的矩形路径（子菜单窗口用：左缘与父面板拼成直边，右侧圆角）。</summary>
    public static GraphicsPath RightRoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (r.Width <= 0 || r.Height <= 0)
        {
            return path;
        }

        float maxR = Math.Min(r.Width, r.Height) / 2f;
        float actualR = Math.Min(radius, maxR);
        if (actualR <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }

        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        float p = Math.Min(actualR * 1.25f, maxR);

        path.AddLine(x, y, x + w - p, y);
        path.AddBezier(
            x + w - p, y,
            x + w - p * 0.45f, y,
            x + w, y + p * 0.45f,
            x + w, y + p);
        path.AddLine(x + w, y + p, x + w, y + h - p);
        path.AddBezier(
            x + w, y + h - p,
            x + w, y + h - p * 0.45f,
            x + w - p * 0.45f, y + h,
            x + w - p, y + h);
        path.AddLine(x + w - p, y + h, x, y + h);
        path.CloseFigure();
        return path;
    }

    private static void AddContinuousCorners(GraphicsPath path, float x, float y, float w, float h, float r)
    {
        float maxP = Math.Min(w, h) / 2f;
        float p = Math.Min(r * 1.25f, maxP);

        // 顶边
        path.AddLine(x + p, y, x + w - p, y);
        // 右上角
        path.AddBezier(
            x + w - p, y,
            x + w - p * 0.45f, y,
            x + w, y + p * 0.45f,
            x + w, y + p);
        // 右边
        path.AddLine(x + w, y + p, x + w, y + h - p);
        // 右下角
        path.AddBezier(
            x + w, y + h - p,
            x + w, y + h - p * 0.45f,
            x + w - p * 0.45f, y + h,
            x + w - p, y + h);
        // 底边
        path.AddLine(x + w - p, y + h, x + p, y + h);
        // 左下角
        path.AddBezier(
            x + p, y + h,
            x + p * 0.45f, y + h,
            x, y + h - p * 0.45f,
            x, y + h - p);
        // 左边
        path.AddLine(x, y + h - p, x, y + p);
        // 左上角
        path.AddBezier(
            x, y + p,
            x, y + p * 0.45f,
            x + p * 0.45f, y,
            x + p, y);
    }
}
