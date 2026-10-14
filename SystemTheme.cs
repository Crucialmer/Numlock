using Microsoft.Win32;

namespace Numlock;

/// <summary>系统主题探测（与图标绘制解耦，便于自检时单独控制）。</summary>
internal static class SystemTheme
{
    /// <summary>系统是否使用浅色主题（浅色任务栏需要黑色图标）。</summary>
    public static bool UsesLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("SystemUsesLightTheme") is int v)
            {
                return v != 0;
            }
        }
        catch
        {
        }
        return false; // 默认按深色任务栏处理（Win10/11 默认）
    }
}
