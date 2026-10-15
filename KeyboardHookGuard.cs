using System.Runtime.InteropServices;

namespace Numlock;

/// <summary>
/// 低级键盘钩子看护器（WH_KEYBOARD_LL）：
/// 保持常开时拦截物理 NumLock 按键，放行合成注入事件；支持 60s 周期自愈与故障降级。
/// </summary>
internal sealed class KeyboardHookGuard : IDisposable
{
    private const int HookRebuildIntervalMs = 60_000;

    private IntPtr _hookHandle;
    private Native.LowLevelKeyboardProc? _hookProc;
    private bool _hookInstallFailed;
    private long _lastHookRebuildMs;
    private readonly Func<bool> _isEnforceEnabled;
    private readonly Action<string, string, ToolTipIcon> _notify;

    public bool HookInstalled => _hookHandle != IntPtr.Zero;

    public KeyboardHookGuard(Func<bool> isEnforceEnabled, Action<string, string, ToolTipIcon> notify)
    {
        _isEnforceEnabled = isEnforceEnabled;
        _notify = notify;
    }

    public void EnsureHook()
    {
        if (_hookHandle != IntPtr.Zero || _hookInstallFailed)
        {
            return;
        }
        _hookProc ??= HookCallback;
        _hookHandle = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _hookProc, Native.GetModuleHandleW(null), 0);
        if (_hookHandle == IntPtr.Zero)
        {
            _hookInstallFailed = true;
            _notify("键盘拦截未生效", "低级键盘钩子安装失败，已降级为轮询纠偏（按键可能闪断）。", ToolTipIcon.Warning);
        }
        else
        {
            _lastHookRebuildMs = Environment.TickCount64;
        }
    }

    public void UninstallHook()
    {
        _hookInstallFailed = false;
        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }
        _ = Native.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    public void RebuildHookIfDue()
    {
        if (_hookHandle == IntPtr.Zero || Environment.TickCount64 - _lastHookRebuildMs < HookRebuildIntervalMs)
        {
            return;
        }
        UninstallHook();
        EnsureHook();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && _isEnforceEnabled())
            {
                int msg = wParam.ToInt32();
                if (Marshal.ReadInt32(lParam) == Native.VK_NUMLOCK)
                {
                    var kb = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
                    if (IsSwallowTarget(msg, kb.vkCode, kb.flags))
                    {
                        return (IntPtr)1;
                    }
                }
            }
        }
        catch
        {
        }
        return Native.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public static bool IsSwallowTarget(int msg, uint vkCode, uint flags)
    {
        if (vkCode != Native.VK_NUMLOCK)
        {
            return false;
        }
        if (msg != Native.WM_KEYDOWN && msg != Native.WM_SYSKEYDOWN
            && msg != Native.WM_KEYUP && msg != Native.WM_SYSKEYUP)
        {
            return false;
        }
        return (flags & Native.LLKHF_INJECTED) == 0;
    }

    public void Dispose()
    {
        UninstallHook();
    }
}
