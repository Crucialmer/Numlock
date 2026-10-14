using Microsoft.Win32;

namespace Numlock;

/// <summary>开机自启（HKCU\...\CurrentVersion\Run），无需管理员权限。</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Numlock";

    public static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(ValueName) is string value && ExtractPath(value) is string path)
            {
                return PathEquals(path, ExePath);
            }
        }
        catch
        {
        }
        return false;
    }

    /// <summary>取 Run 值中的可执行文件路径（去掉引号包裹与命令行参数）。</summary>
    internal static string? ExtractPath(string value)
    {
        value = value.Trim();
        if (value.StartsWith('"'))
        {
            int end = value.IndexOf('"', 1);
            return end > 1 ? value[1..end] : null;
        }
        int exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe >= 0)
        {
            return value[..(exe + 4)];
        }
        return value.Length > 0 ? value : null;
    }

    internal static bool PathEquals(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{ExePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
