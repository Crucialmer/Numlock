using System.Drawing;
using System.Windows.Forms;

namespace Numlock;

/// <summary>
/// macOS 风格托盘弹出菜单：
/// 1. 重写 MaxItemSize 强制所有条目填满标准宽度（紧凑 168px+，自适应 DPI，呈现黄金高宽比）；
/// 2. 统一承载 MacMenuRenderer，消除传统 Windows 菜单左侧灰边。
/// </summary>
internal sealed class MacContextMenuStrip : ContextMenuStrip
{
    public MacContextMenuStrip(MacMenuRenderer renderer)
    {
        Renderer = renderer;
        ShowImageMargin = false;
        ShowCheckMargin = true;
        Padding = new Padding(4, 6, 4, 6);
    }

    protected override Size MaxItemSize
    {
        get
        {
            float scale = DeviceDpi / 96f;
            int minW = (int)Math.Round(168 * scale);
            return new Size(Math.Max(base.MaxItemSize.Width, minW), base.MaxItemSize.Height);
        }
    }
}
