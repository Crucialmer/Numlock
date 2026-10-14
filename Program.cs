namespace Numlock;

internal static class Program
{
    internal const string MutexName = @"Local\Numlock_Tray_SingleInstance";
    private const string ShowEventName = @"Local\Numlock_Tray_ShowEvent";
    private const string AckEventName = @"Local\Numlock_Tray_AckEvent";

    [STAThread]
    private static void Main(string[] args)
    {
        // UI 线程未处理异常兜底：常驻托盘工具不应因一次异常（自绘/菜单回调/GDI+ 偶发失败）整体退出
        // —— 与代码里 Watch/RefreshIcon 等入口的 try/catch 保持同一不变式。须在创建任何窗口前设置。
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => TrayApp.ReportUnhandledUiException(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => TrayApp.LogUnhandled(e.ExceptionObject as Exception);

        if (HasSwitch(args, "--selftest"))
        {
            ApplicationConfiguration.Initialize();
            SelfTest.Run(GetArg(args, "--selftest") ?? "selftest");
            return;
        }

        if (HasSwitch(args, "--genico"))
        {
            ApplicationConfiguration.Initialize();
            Environment.ExitCode = AppIcoBuilder.Run(GetArg(args, "--genico") ?? Path.Combine("assets", "Numlock.ico"));
            return;
        }

        // 更新辅助模式：由主实例在退出前拉起，等旧进程退出后把暂存的新版复制覆盖过去。
        // 必须在 Mutex 之前处理——此时旧实例可能还没完全退出。
        if (HasSwitch(args, Updater.ApplySwitch))
        {
            Environment.ExitCode = Updater.RunApplyHelper(ArgsAfter(args, Updater.ApplySwitch));
            return;
        }

        // 命令行检查更新（开发/排障用）：0=已最新 1=有新版本 2=检查失败
        if (HasSwitch(args, Updater.CheckSwitch))
        {
            var (result, info) = Updater.Check();
            string line = result switch
            {
                UpdateCheckResult.Available => $"发现新版本 {info!.Version.ToString(3)}（当前 {Updater.CurrentVersion.ToString(3)}）：{info.PageUrl}",
                UpdateCheckResult.UpToDate => $"已是最新版本（当前 {Updater.CurrentVersion.ToString(3)}，线上 {info?.Version.ToString(3)}）",
                _ => "检查更新失败（网络不可达或响应异常）",
            };
            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // 无控制台（双击启动）时忽略
            }
            Environment.ExitCode = result switch
            {
                UpdateCheckResult.UpToDate => 0,
                UpdateCheckResult.Available => 1,
                _ => 2,
            };
            return;
        }

        // 先建/打开命名事件再判 Mutex：避免“已有实例尚未建好事件”的窗口期内通知丢失；
        // 第二实例 Set 该事件，首实例看护循环弹一次“已在运行”气泡并 Set ack；
        // 收不到 ack（首实例正在自检 / 看护线程已死 / 卡在模态对话框）则自己给提示——
        // 否则用户双击后毫无反应（无窗口、无气泡、退出码 0），会以为程序坏了。
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        using var ackEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AckEventName);
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try { showEvent.Set(); } catch { }
            bool acknowledged = false;
            try { acknowledged = ackEvent.WaitOne(1500); } catch { }
            if (!acknowledged)
            {
                try
                {
                    MessageBox.Show(
                        "Numlock 已在运行（或正在自检）。\n\n右键系统托盘中的键帽图标可打开菜单。",
                        "Numlock",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch
                {
                    // 提示失败也不影响：本次启动就此返回
                }
            }
            return;
        }

        ApplicationConfiguration.Initialize();
        using var app = new TrayApp(showEvent, ackEvent);
        Application.Run(app);
    }

    private static bool HasSwitch(string[] args, string name) =>
        args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>取开关之后的全部参数（开关本身不含在内）。</summary>
    private static string[] ArgsAfter(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[(i + 1)..] : Array.Empty<string>();
    }

    private static string? GetArg(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return args[i + 1];
        }
        return null;
    }
}
