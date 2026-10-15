using System.Runtime.InteropServices;

namespace Numlock;

/// <summary>NumLock（数字小键盘）状态的读取与设置。</summary>
internal static class NumLock
{
    /// <summary>
    /// 键帽开关状态的权威读法：GetKeyState 低位。
    /// 勿用 GetAsyncKeyState 低位——那是“自上次读取后是否按过”的一次性脉冲，轮询会读到即清。
    /// </summary>
    public static bool IsOn => (Native.GetKeyState(Native.VK_NUMLOCK) & 1) == 1;

    /// <summary>
    /// 把 NumLock 校正到目标状态，返回是否达到目标。
    /// 优化等待机制：采用 1ms 高精度短片等待代替 5ms 粗颗粒休眠，最小化 UI 消息泵延迟；
    /// 每次注入后等待状态翻转生效，再以二次确认判定成功。
    /// </summary>
    public static bool Ensure(bool desired, int attempts = 2, int settleMs = 20, int flipWaitMs = 80)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (Settled(desired, settleMs))
            {
                return true;
            }
            bool before = IsOn;
            Toggle();
            // 等这次翻转真正生效（状态读数不同于注入前），再进入下一轮评估
            long deadline = Environment.TickCount64 + flipWaitMs;
            while (IsOn == before && Environment.TickCount64 < deadline)
            {
                Thread.Sleep(1);
            }
        }
        return Settled(desired, settleMs);
    }

    /// <summary>异步非阻塞校正 NumLock 到目标状态（提供现代异步流支持）。</summary>
    public static async ValueTask<bool> EnsureAsync(bool desired, int attempts = 2, int settleMs = 20, int flipWaitMs = 80, CancellationToken ct = default)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (IsOn == desired)
            {
                if (settleMs > 0)
                {
                    await Task.Delay(settleMs, ct).ConfigureAwait(false);
                }
                if (IsOn == desired)
                {
                    return true;
                }
            }
            bool before = IsOn;
            Toggle();
            long deadline = Environment.TickCount64 + flipWaitMs;
            while (IsOn == before && Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
            {
                await Task.Delay(2, ct).ConfigureAwait(false);
            }
        }
        if (IsOn != desired)
        {
            return false;
        }
        if (settleMs > 0)
        {
            await Task.Delay(settleMs, ct).ConfigureAwait(false);
        }
        return IsOn == desired;
    }

    /// <summary>状态是否已达目标且稳定：首次读取即不符则不等待；相符时再隔一段确认一次。</summary>
    private static bool Settled(bool desired, int settleMs)
    {
        if (IsOn != desired)
        {
            return false;
        }
        if (settleMs > 0)
        {
            Thread.Sleep(settleMs);
        }
        return IsOn == desired;
    }

    /// <summary>发送一次 NumLock 切换（须带扩展键标志），优先 SendInput，失败退回 keybd_event。</summary>
    public static void Toggle()
    {
        var inputs = new Native.INPUT[2];
        inputs[0].type = Native.INPUT_KEYBOARD;
        inputs[0].u.ki = new Native.KEYBDINPUT
        {
            wVk = Native.VK_NUMLOCK,
            wScan = 0x45,
            dwFlags = Native.KEYEVENTF_EXTENDEDKEY,
        };
        inputs[1].type = Native.INPUT_KEYBOARD;
        inputs[1].u.ki = new Native.KEYBDINPUT
        {
            wVk = Native.VK_NUMLOCK,
            wScan = 0x45,
            dwFlags = Native.KEYEVENTF_EXTENDEDKEY | Native.KEYEVENTF_KEYUP,
        };

        // 仅在“一个事件都没注入成功”时整对兜底；部分成功（返回 1）时不能再补发整对，
        // 否则会多按一次造成双重切换——状态收敛交给 Ensure 的检查循环。
        if (Native.SendInput(2, inputs, Marshal.SizeOf<Native.INPUT>()) == 0)
        {
            Native.keybd_event(Native.VK_NUMLOCK, 0x45, Native.KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            Native.keybd_event(Native.VK_NUMLOCK, 0x45, Native.KEYEVENTF_EXTENDEDKEY | Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }
}
