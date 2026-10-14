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
    /// 单次尝试最坏 ≈ settleMs 二次确认 + 注入 + flipWaitMs 等翻转生效，默认 2 次尝试最坏 ≈200ms；
    /// 调用方多在 UI 线程（低级键盘钩子回调也在 UI 线程派发），阻塞过长可能触发 Windows 的钩子
    /// 回调超时（≈300ms）导致钩子被静默摘除——看护循环因此用 attempts:1, settleMs:12，
    /// 并在上一次失败时把 flipWaitMs 收到 25ms（单次阻塞 ≈35ms），长重试交给下一个看护周期。
    /// 每次注入后先等“翻转已生效”（高负载下注入可能滞后数十毫秒），
    /// 再以“二次读取确认”判定成功——避免把在途翻转当成完成，或重复注入相互抵消。
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
                Thread.Sleep(5);
            }
        }
        return Settled(desired, settleMs);
    }

    /// <summary>状态是否已达目标且稳定：首次读取即不符则不等待；相符时再隔一段确认一次。</summary>
    private static bool Settled(bool desired, int settleMs)
    {
        if (IsOn != desired)
        {
            return false;
        }
        Thread.Sleep(settleMs);
        return IsOn == desired;
    }

    /// <summary>发送一次 NumLock 切换（须带扩展键标志），优先 SendInput，失败退回 keybd_event。</summary>
    private static void Toggle()
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
