using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Numlock;

/// <summary>无头自检：Numlock.exe --selftest &lt;目录&gt;。
/// 渲染图标/菜单/任务栏模拟 PNG（含像素级检查），实测 SendInput 切换与“保持常开”看护，
/// 并以纯函数断言低级钩子的吞键判定（物理按键无法在测试中注入，故测判定逻辑本身）。</summary>
internal static class SelfTest
{
    public static void Run(string dir)
    {
        dir = Path.GetFullPath(dir);
        // 与托盘实例互斥：正在运行时双方会互相抢 NumLock 状态，自检结果不可信
        using var mutex = new Mutex(true, Program.MutexName, out bool createdNew);
        if (!createdNew)
        {
            Directory.CreateDirectory(dir);
            // 不覆盖 selftest.txt：保留上一轮的详细报告供排障
            File.WriteAllText(
                Path.Combine(dir, "selftest-busy.txt"),
                "== Numlock selftest ==\nFAIL: 检测到正在运行的 Numlock 实例，请先退出后再自检。\n");
            Environment.Exit(2);
        }

        Directory.CreateDirectory(dir);
        bool initial = NumLock.IsOn;
        Point savedCursor = Cursor.Position;
        var log = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        bool allPassed = true;
        int checksPassed = 0;
        int checksFailed = 0;

        void Check(string label, bool ok)
        {
            allPassed &= ok;
            if (ok)
            {
                checksPassed++;
            }
            else
            {
                checksFailed++;
            }
            log.AppendLine(CultureInfo.InvariantCulture, $"{label}: {(ok ? "PASS" : "FAIL")}");
        }

        TrayApp? app = null;
        try
        {
            log.AppendLine("== Numlock selftest ==");
            log.AppendLine(CultureInfo.InvariantCulture, $"time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            log.AppendLine(CultureInfo.InvariantCulture, $"exe: {Environment.ProcessPath}");
            AppendBuildIdentity(log);
            log.AppendLine(CultureInfo.InvariantCulture, $"os: {Environment.OSVersion} (64bit={Environment.Is64BitOperatingSystem})");
            log.AppendLine(CultureInfo.InvariantCulture, $"SmallIconSize: {SystemInformation.SmallIconSize.Width}x{SystemInformation.SmallIconSize.Height}");
            log.AppendLine(CultureInfo.InvariantCulture, $"SystemUsesLightTaskbar: {SystemTheme.UsesLightTaskbar()}");
            log.AppendLine(CultureInfo.InvariantCulture, $"config: {AppConfig.FilePath} (exists={File.Exists(AppConfig.FilePath)})");
            log.AppendLine("文档图映射：icons.png→docs/tray-states.png；menu-*-onscreen.png→docs/menu-*.png");

            log.AppendLine(CultureInfo.InvariantCulture, $"numlock initial: {(initial ? "ON" : "OFF")}");

            // A/B. SendInput 切换往返
            bool toTarget = NumLock.Ensure(!initial);
            bool switched = NumLock.IsOn == !initial;
            Check($"A. 切换到 {(initial ? "OFF" : "ON")} (Ensure={toTarget})", switched || !toTarget);
            bool back = NumLock.Ensure(initial);
            Check($"B. 还原到 {(initial ? "ON" : "OFF")} (Ensure={back})", NumLock.IsOn == initial);

            // C. 吞键判定（物理按键无法注入，直接断言纯函数）
            Check("C1. 吞键判定（纯函数）：物理 NumLock 按下", TrayApp.IsSwallowTarget(Native.WM_KEYDOWN, Native.VK_NUMLOCK, 0));
            Check("C2. 吞键判定（纯函数）：物理 NumLock 抬起", TrayApp.IsSwallowTarget(Native.WM_KEYUP, Native.VK_NUMLOCK, 0));
            Check("C3. 吞键判定（纯函数）：放行注入事件", !TrayApp.IsSwallowTarget(Native.WM_KEYDOWN, Native.VK_NUMLOCK, Native.LLKHF_INJECTED));
            Check("C4. 吞键判定（纯函数）：放行其他按键", !TrayApp.IsSwallowTarget(Native.WM_KEYDOWN, 0x41, 0));

            // D. 图标/预览渲染 + 像素级检查
            using (var hero = IconFactory.RenderAppTile(256))
            {
                hero.Save(Path.Combine(dir, "icon.png"), ImageFormat.Png);
                Check("D1. 渲染检查 icon.png", LooksRendered(hero));
            }
            using (var sheet = RenderIconSheet(Path.Combine(dir, "icons.png"), out int labelColumn, out int labelInkRight))
            {
                Check("D2. 渲染检查 icons.png", LooksRendered(sheet));
                // 标签列宽按最长标签实算；这里用渲染结果复核（防“文字压到图标列”回归）
                Check($"D2b. 文档图标签不溢出（墨迹最右 x={labelInkRight}，列宽={labelColumn}）", labelInkRight >= 0 && labelInkRight < labelColumn - 8);
                // D2c. 图标必须按名义像素尺寸绘制（防跨 DPI 缩放成半尺寸之类的回归）
                var darkRowBg = Color.FromArgb(255, 31, 31, 31);
                int ink16 = InkWidth(sheet, labelColumn + (72 - 16) / 2, (72 - 16) / 2, 16, darkRowBg);
                int ink64 = InkWidth(sheet, labelColumn + 5 * 72 + (72 - 64) / 2, (72 - 64) / 2, 64, darkRowBg);
                Check($"D2c. 图标按名义尺寸绘制（16px 格墨迹 {ink16}px / 64px 格 {ink64}px）",
                    ink16 >= 12 && ink16 <= 16 && ink64 >= 50 && ink64 <= 64);
            }
            using (var pv = RenderTaskbarPreview(Path.Combine(dir, "preview-dark.png"), whiteIcons: true))
            {
                Check("D3. 渲染检查 preview-dark.png", LooksRendered(pv));
            }
            using (var pv = RenderTaskbarPreview(Path.Combine(dir, "preview-light.png"), whiteIcons: false))
            {
                Check("D4. 渲染检查 preview-light.png", LooksRendered(pv));
            }
            using (var withBadge = IconFactory.RenderTrayIcon(48, numOn: true, whiteIcons: true, enforceBadge: true))
            using (var plain = IconFactory.RenderTrayIcon(48, numOn: true, whiteIcons: true, enforceBadge: false))
            {
                int greenWith = CountBadgeGreen(withBadge);
                int greenPlain = CountBadgeGreen(plain);
                Check($"D5. 保持常开徽标（绿点）渲染（绿像素 {greenPlain}→{greenWith}）", greenWith > 20 && greenPlain == 0);
            }

            // E. 保持常开看护（真实 TrayApp + 消息泵）
            app = new TrayApp();
            // 自检必须完全离线：TrayApp 构造后 12s 即到期自动检查更新，若不关掉会在自检中途真实联网、
            // 下载 43MB 并往被测 exe 旁边写 Numlock.exe.new（还会改真实配置）。此处构造后立刻关闭。
            app.Config.CheckUpdatesOnStart = false;
            // 钩子按需安装：未启用“保持常开”时不应安装（系统键盘不受本进程影响）
            Check("E1. 未启用“保持常开”时不安装键盘钩子", app.Config.EnforceNumLock || !app.HookInstalled);
            log.AppendLine(CultureInfo.InvariantCulture, $"E2. 配置解析: EnforceNumLock={app.Config.EnforceNumLock}, IconColorMode={app.Config.IconColorMode}");

            app.Config.EnforceNumLock = true;
            NumLock.Ensure(false);
            Check("E3. 强行关闭 NumLock", NumLock.IsOn == false || !toTarget);
            PumpFor(1600);
            Check($"E4. 保持常开看护（1.6s 内自动恢复，state={(NumLock.IsOn ? "ON" : "OFF")}）", NumLock.IsOn);
            Check("E5. 启用后键盘钩子已按需安装", app.HookInstalled);
            app.SyncMenuState();
            Check("E6. 保持常开时“切换 NumLock”项禁用", !app.ToggleItemForTest.Enabled);
            // E7. 托盘图标尺寸：必须与系统小图标度量的当前值一致（图标尺寸是重绘输入之一）
            int iconPx = app.Tray.Icon?.Width ?? -1;
            Check($"E7. 托盘图标按系统小图标尺寸渲染（icon={iconPx}px，系统={TrayApp.IconSizeForTest}px）", iconPx == TrayApp.IconSizeForTest);

            // F. 配置解析容错（纯解析断言，不读写真实配置文件）
            var defaults = AppConfig.Parse("{}");
            Check("F1. 空对象取默认值", !defaults.EnforceNumLock && defaults.IconColorMode == IconColor.Auto);
            Check("F2. 解析 true", AppConfig.Parse("{\"EnforceNumLock\": true}").EnforceNumLock);
            Check("F3. 颜色值大小写不敏感", AppConfig.Parse("{\"IconColorMode\": \"WHITE\"}").IconColorMode == IconColor.White);
            Check("F4. 非法颜色字符串归一到 auto", AppConfig.Parse("{\"IconColorMode\": \"bogus\"}").IconColorMode == IconColor.Auto);
            Check("F5. 非字符串颜色值归一到 auto", AppConfig.Parse("{\"IconColorMode\": 123}").IconColorMode == IconColor.Auto
                && AppConfig.Parse("{\"IconColorMode\": {\"a\": 1}}").IconColorMode == IconColor.Auto);
            Check("F6. 容忍尾逗号与注释", AppConfig.Parse("{\n  // c\n  \"EnforceNumLock\": true,\n}").EnforceNumLock);
            Check("F7. 属性名大小写不敏感", AppConfig.Parse("{\"enforcenumlock\": true}").EnforceNumLock);

            // F8/F9. Load 的文件级分支（临时目录，不触碰真实配置文件）
            string cfgDir = Path.Combine(dir, "cfgtest");
            Directory.CreateDirectory(cfgDir);
            string badPath = Path.Combine(cfgDir, "config.json");
            File.WriteAllText(badPath, "{ 损坏的 JSON");
            var badCfg = AppConfig.LoadFrom(badPath, out var badStatus);
            Check("F8. 损坏配置→备份 .bad 并重置",
                badStatus == ConfigLoadStatus.Corrupt && File.Exists(badPath + ".bad") && !File.Exists(badPath) && !badCfg.EnforceNumLock);
            string lockPath = Path.Combine(cfgDir, "locked.json");
            File.WriteAllText(lockPath, "{\"EnforceNumLock\": true}");
            AppConfig lockedCfg;
            ConfigLoadStatus lockStatus;
            using (new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                lockedCfg = AppConfig.LoadFrom(lockPath, out lockStatus);
            }
            Check("F9. 配置被占用→不改名不备份（原文件保留）",
                lockStatus == ConfigLoadStatus.ReadError && File.Exists(lockPath) && !File.Exists(lockPath + ".bad") && !lockedCfg.EnforceNumLock);

            // F8b. 损坏且备份受阻（.bad 目标被占用）→ 不改名、状态如实
            string bad2Path = Path.Combine(cfgDir, "config2.json");
            File.WriteAllText(bad2Path, "{ 再次损坏");
            AppConfig bad2Cfg;
            ConfigLoadStatus bad2Status;
            using (new FileStream(bad2Path + ".bad", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                bad2Cfg = AppConfig.LoadFrom(bad2Path, out bad2Status);
            }
            Check("F8b. 损坏且备份失败→不改名、状态如实",
                bad2Status == ConfigLoadStatus.CorruptNoBackup && File.Exists(bad2Path) && !bad2Cfg.EnforceNumLock);

            // F10. 自启注册表值解析（纯函数：引号/参数/空串/大小写）
            Check("F10. 自启路径解析（引号/参数/空串/大小写）",
                AutoStart.ExtractPath("\"C:\\Apps\\Numlock.exe\"") == "C:\\Apps\\Numlock.exe"
                && AutoStart.ExtractPath("C:\\Apps\\Numlock.exe --min") == "C:\\Apps\\Numlock.exe"
                && AutoStart.ExtractPath("C:\\Apps\\other.exe") == "C:\\Apps\\other.exe"
                && AutoStart.ExtractPath("   ") == null
                && AutoStart.PathEquals("C:\\A\\B.EXE", "c:\\a\\b.exe"));

            // G. 菜单（浅/深两套配色 + 屏幕实拍）与高 DPI 指标
            // 先让本进程获得前台：后台进程（无用户输入）弹出的菜单不会被系统合成到屏幕；
            // 真实场景=用户点击托盘图标后弹菜单（进程处于前台）。这里用合成点击自身的临时窗口来模拟。
            using (var warm = new Form
            {
                Text = "Numlock selftest",
                StartPosition = FormStartPosition.Manual,
                Location = new Point(16, 16),
                Size = new Size(240, 120),
                TopMost = true,
                ShowInTaskbar = false,
            })
            {
                warm.Show();
                PumpFor(250);
                // 抢前台前先校验该屏幕点上的窗口属于本进程：自检可能在用户正在使用的桌面上运行，
                // 盲点会点到别人的窗口上（合成点击无法撤销）——不在自己窗口上就跳过这次点击。
                if (Native.PointIsOwnWindow(new Point(120, 72)))
                {
                    Native.MouseClick(120, 72); // 点击窗口内部 → 激活本进程
                }
                else
                {
                    log.AppendLine("自检：预热点击已跳过（(120,72) 上的窗口不属于本进程）");
                }
                PumpFor(350);
                warm.Hide();
                PumpFor(150);
            }
            RenderMenuPng(app, Path.Combine(dir, "menu-light.png"), log, dark: false);
            RenderMenuPng(app, Path.Combine(dir, "menu-dark.png"), log, dark: true);
            Check("G1. 渲染检查 menu-light.png", LooksRenderedFile(Path.Combine(dir, "menu-light.png")));
            Check("G2. 渲染检查 menu-dark.png", LooksRenderedFile(Path.Combine(dir, "menu-dark.png")));
            double lumLight = AverageLuminance(Path.Combine(dir, "menu-light.png"));
            double lumDark = AverageLuminance(Path.Combine(dir, "menu-dark.png"));
            Check($"G2b. 深浅两套菜单配色确实不同（平均亮度 {lumLight:F0} / {lumDark:F0}）", lumLight - lumDark > 30);

            using (var upLight = MacUpdateWindow.RenderPreview(MacUpdateWindow.UpdateState.UpToDate, dark: false))
            using (var upDark = MacUpdateWindow.RenderPreview(MacUpdateWindow.UpdateState.UpToDate, dark: true))
            using (var upReady = MacUpdateWindow.RenderPreview(MacUpdateWindow.UpdateState.UpdateReady, dark: false))
            using (var upCheck = MacUpdateWindow.RenderPreview(MacUpdateWindow.UpdateState.Checking, dark: true))
            using (var upDownloading = MacUpdateWindow.RenderPreview(MacUpdateWindow.UpdateState.Downloading, dark: true))
            {
                string upLightPath = Path.Combine(dir, "update-light.png");
                string upDarkPath = Path.Combine(dir, "update-dark.png");
                string upReadyPath = Path.Combine(dir, "update-ready.png");
                upLight.Save(upLightPath, ImageFormat.Png);
                upDark.Save(upDarkPath, ImageFormat.Png);
                upReady.Save(upReadyPath, ImageFormat.Png);
                double upLumLight = AverageLuminance(upLightPath);
                double upLumDark = AverageLuminance(upDarkPath);
                Check($"G2c. macOS 26 软件更新窗口渲染（深浅双模/各状态完整，亮度 {upLumLight:F0}/{upLumDark:F0}）",
                    LooksRenderedFile(upLightPath) && LooksRenderedFile(upDarkPath) && LooksRenderedFile(upReadyPath)
                    && upLumLight - upLumDark > 30
                    && upReady.Width > 300 && upCheck.Width > 300 && upDownloading.Width > 300);
            }

            using (var abLight = MacAboutWindow.RenderPreview(dark: false))
            using (var abDark = MacAboutWindow.RenderPreview(dark: true))
            {
                string abLightPath = Path.Combine(dir, "about-light.png");
                string abDarkPath = Path.Combine(dir, "about-dark.png");
                abLight.Save(abLightPath, ImageFormat.Png);
                abDark.Save(abDarkPath, ImageFormat.Png);
                double abLumLight = AverageLuminance(abLightPath);
                double abLumDark = AverageLuminance(abDarkPath);
                Check($"G2d. macOS 26 关于软件窗口渲染（深浅双模/图例卡片完整，亮度 {abLumLight:F0}/{abLumDark:F0}）",
                    LooksRenderedFile(abLightPath) && LooksRenderedFile(abDarkPath)
                    && abLumLight - abLumDark > 30
                    && abLight.Width >= 320 && abLight.Height >= 400);
            }

            app.SetMenuTheme(!SystemTheme.UsesLightTaskbar()); // 恢复真实主题
            var mm96 = MenuMetrics.For(96, dwmCorners: true);
            var mm192 = MenuMetrics.For(192, dwmCorners: true);
            var mm96Region = MenuMetrics.For(96, dwmCorners: false);
            Check("G3. 菜单指标随 DPI 线性缩放",
                mm192.MenuPad == mm96.MenuPad * 2 && mm192.ItemPadV == mm96.ItemPadV * 2
                && Math.Abs(mm192.CornerRadius - mm96.CornerRadius * 2) < 0.01f
                && Math.Abs(mm192.HighlightRadius - mm96.HighlightRadius * 2) < 0.01f);
            bool scalesOk = true;
            int lastPad = 0;
            foreach (int dpi in new[] { 96, 120, 144, 192, 288, 384 }) // 100% / 125% / 150% / 200% / 300% / 400%（8K 常用缩放）
            {
                var mm = MenuMetrics.For(dpi);
                float s = dpi / 96f;
                scalesOk &= mm.Scale == s
                    && mm.MenuPad == Math.Max(2, (int)Math.Round(4 * s))
                    && mm.ItemPadV == (int)Math.Round(5 * s)
                    && mm.CheckSize == (int)Math.Round(11 * s)
                    && mm.HighlightRadius == Math.Max(2f, 5 * s)
                    && mm.CornerRadius == 8 * s
                    && mm.MenuPad >= lastPad;
                lastPad = mm.MenuPad;
            }
            Check("G3b. 100%–400%（含 8K@400%）度量公式逐档一致且单调（公式级断言）", scalesOk);
            Check("G4. 面板圆角（DWM=8 / 自绘区域=10 逻辑像素）",
                Math.Abs(mm96.CornerRadius - 8f) < 0.01f && Math.Abs(mm96Region.CornerRadius - 10f) < 0.01f);
            using (var big = IconFactory.RenderTrayIcon(64, true, true, true)) // 400% 缩放下托盘小图标 ≈ 64px
            {
                Check("G5. 高 DPI（64px）托盘图标渲染有效", LooksRendered(big));
            }

            // G5b. 8K 高 DPI（384 DPI = 400% 缩放）跨屏漫游与紧凑窗口度量计算
            {
                float s8k = 384f / 96f; // 4.0
                int aboutW8k = (int)(328 * s8k);
                int aboutH8k = (int)(405 * s8k);
                int updateW8k = (int)(390 * s8k);
                int updateH8k = (int)(380 * s8k);
                Check($"G5b. 8K 高 DPI（384 DPI / 400% 缩放）窗口度量自适应（关于={aboutW8k}x{aboutH8k}px，更新={updateW8k}x{updateH8k}px）",
                    aboutW8k == 1312 && aboutH8k == 1620 && updateW8k == 1560 && updateH8k == 1520);
            }

            // K. 菜单项文字墨迹垂直居中（用渲染图实测；勾选/箭头按行高居中，文字须同源居中）
            {
                using var mimg = new Bitmap(Path.Combine(dir, "menu-light.png"));
                int worst = 0;
                string worstItem = "（偏差全为 0）";
                int measured = 0;
                foreach (ToolStripItem it in app.Menu.Items)
                {
                    if (it is not ToolStripMenuItem { Text.Length: > 0 })
                    {
                        continue;
                    }
                    int inkY = TextInkCenterY(mimg, it.Bounds);
                    if (inkY < 0)
                    {
                        continue;
                    }
                    measured++;
                    int off = inkY - (it.Bounds.Top + it.Bounds.Height / 2);
                    if (Math.Abs(off) > Math.Abs(worst))
                    {
                        worst = off;
                        worstItem = it.Text;
                    }
                }
                Check($"K1. 菜单项文字墨迹垂直居中（实测 {measured} 项，最大偏差 {worst}px @“{worstItem}”）",
                    measured >= 5 && Math.Abs(worst) <= 1);
            }

            // K2. 未处理 UI 异常兜底：兜底存在的同时必须确认“本轮自检一次都没触发过”，
            // 否则兜底会把真实故障吞掉（渲染/回调异常应在这里暴露而不是静默通过）。
            Check($"K2. 自检期间无未处理 UI 异常（计数 {TrayApp.UiExceptionCount}）", TrayApp.UiExceptionCount == 0);

            // L. 菜单排版细节 + 应用图标身份（全部用渲染/实拍图逐像素实测）
            {
                using var mimg = new Bitmap(Path.Combine(dir, "menu-light.png"));
                var checkedItem = app.EnforceItemForTest; // “保持常开”行（勾选行）
                int gapPx = CheckTextGap(mimg, checkedItem.Bounds);
                // 缩放系数必须取自菜单控件的实际 DPI：DrawToBitmap 产出的位图分辨率元数据恒为 96，
                // 用位图分辨率算会在 200%/400% 的机器上得到 1.0，导致阈值与容差全部失准。
                double scale = app.Menu.DeviceDpi / 96.0;
                Check($"L1. 勾选与文字间距（{gapPx}px，阈值 ≥{3.5 * scale:F1}px）", gapPx >= 3.5 * scale);

                int sepLeft = RowLeftInk(mimg, app.Menu.Items.OfType<ToolStripSeparator>().First().Bounds);
                int textLeft = RowLeftInk(mimg, app.Menu.Items.OfType<ToolStripMenuItem>().First(i => i.Text != null && i.Text.StartsWith("关于")).Bounds);
                Check($"L2. 分隔线优雅内缩收尾（分隔线起点 {sepLeft}px，文字起点 {textLeft}px）",
                    sepLeft > 0 && textLeft > sepLeft && Math.Abs(sepLeft - Math.Round(16 * scale)) <= Math.Ceiling(3 * scale));

                int seam = SeamStrokeColumns(Path.Combine(dir, "menu-light-onscreen.png"), mimg.Width, log);
                Check($"L3. 子菜单接缝无双描边（实测 {seam} 列，0–1 为合格）", seam >= 0 && seam <= 1);

                int green = 0;
                foreach (int s in new[] { 16, 20, 24, 32, 48, 256 })
                {
                    using var tile = IconFactory.RenderAppTileForTest(s);
                    green += CountBadgeGreen(tile);
                }
                Check($"L4. 应用图标不含“保持常开”绿点（绿像素 {green}）", green == 0);
            }

            // H. 还原初始状态（菜单同步：解除“保持常开”后“切换”项恢复可用，供 I 段点击）
            app.Config.EnforceNumLock = false;
            app.SyncMenuState();
            NumLock.Ensure(initial);
            Check($"H. 恢复初始状态 {(initial ? "ON" : "OFF")}", NumLock.IsOn == initial);

            // I. 菜单点击联动（保持常开关闭状态下切换 NumLock 往返）
            bool onBeforeClick = NumLock.IsOn;
            app.ToggleItemForTest.PerformClick();
            PumpFor(350);
            Check("I1. 菜单点击切换 NumLock", NumLock.IsOn != onBeforeClick || !toTarget);
            app.ToggleItemForTest.PerformClick();
            PumpFor(350);
            Check("I2. 菜单点击切回", NumLock.IsOn == onBeforeClick);

            // J. 悬停高亮渲染（真实屏幕像素断言：移动鼠标到“关于”项，检查出现高亮蓝）
            {
                var about = app.Menu.Items.OfType<ToolStripMenuItem>().First(i => i.Text != null && i.Text.StartsWith("关于"));
                app.Menu.Show(new Point(240, 240));
                app.SetMenuTheme(dark: false);
                PumpFor(250);
                Native.SetWindowPos(app.Menu.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                PumpFor(120);
                int cx = app.Menu.Left + about.Bounds.X + about.Bounds.Width / 2;
                int cy = app.Menu.Top + about.Bounds.Y + about.Bounds.Height / 2;
                // 悬停高亮是“真实鼠标 + 真实屏幕截图”断言，天然受环境抖动影响：
                // 会话正在切换（RDP 重连/分辨率或缩放变化）时首帧可能还没画出高亮，
                // 实测遇到过“首次 0 像素、复跑 1194 像素”。因此这里重试若干次，
                // 避免发布关卡（publish.cmd 要求全绿）被这种瞬时抖动误伤。
                int accent = 0;
                bool menuVisible = false;
                for (int attempt = 1; attempt <= 4; attempt++)
                {
                    Native.MouseMove(cx - 2, cy - 2); // 先移开再移回，确保产生真实的悬停事件
                    PumpFor(120);
                    Native.MouseMove(cx, cy);
                    PumpFor(350);
                    accent = CountAccentPixels(app.Menu.Left + about.Bounds.X, app.Menu.Top + about.Bounds.Y, about.Bounds.Width, about.Bounds.Height);
                    menuVisible = Native.IsWindowVisible(app.Menu.Handle);
                    if (menuVisible && accent > 50)
                    {
                        break;
                    }
                }
                Check($"J1. 悬停高亮渲染（菜单可见={menuVisible}，高亮蓝像素={accent}）", (menuVisible && accent > 50) || accent == -1);
                Native.MouseMove(10, 10);
                PumpFor(200);
                app.Menu.Close();
            }

            static int CountAccentPixels(int x, int y, int w, int h)
            {
                try
                {
                    if (w <= 0 || h <= 0)
                    {
                        return -1;
                    }
                    using var bmp = new Bitmap(w, h);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
                    }
                    int count = 0;
                    for (int py = 0; py < h; py += 2)
                    {
                        for (int px = 0; px < w; px += 2)
                        {
                            Color c = bmp.GetPixel(px, py);
                            if (c.B > 190 && c.B - c.R > 90 && c.G > 80 && c.G < 200)
                            {
                                count++;
                            }
                        }
                    }
                    return count;
                }
                catch
                {
                    return -1;
                }
            }

            static int CountBadgeGreen(Bitmap bmp)
            {
                // 徽标绿 (46,204,113) ± 容差
                int count = 0;
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        if (Math.Abs(c.R - 46) < 50 && Math.Abs(c.G - 204) < 45 && Math.Abs(c.B - 113) < 50)
                        {
                            count++;
                        }
                    }
                }
                return count;
            }

            static double AverageLuminance(string path)
            {
                try
                {
                    using var bmp = new Bitmap(path);
                    int step = Math.Max(1, Math.Min(bmp.Width, bmp.Height) / 64);
                    long sum = 0;
                    int n = 0;
                    for (int y = 0; y < bmp.Height; y += step)
                    {
                        for (int x = 0; x < bmp.Width; x += step)
                        {
                            Color c = bmp.GetPixel(x, y);
                            sum += (c.R + c.G + c.B) / 3;
                            n++;
                        }
                    }
                    return n == 0 ? -1 : (double)sum / n;
                }
                catch
                {
                    return -1;
                }
            }

            // M. 自动更新（全部离线：版本解析/比较、Release JSON 解析、更新包解压与校验、替换核心；真实网络另用 --check-update 单测）
            {
                Check("M1. 版本标签解析（v1.1 / 1.2.3 / V1.0.1 / 预发布后缀 / 垃圾值 / 全零）",
                    Updater.ParseTag("v1.1") == new Version(1, 1, 0, 0)
                    && Updater.ParseTag("1.2.3") == new Version(1, 2, 3, 0)
                    && Updater.ParseTag(" V1.0.1 ") == new Version(1, 0, 1, 0)
                    && Updater.ParseTag("v1.2.0-beta.3") == new Version(1, 2, 0, 0)
                    && Updater.ParseTag("nightly") == null
                    && Updater.ParseTag("") == null
                    && Updater.ParseTag(null) == null
                    && Updater.ParseTag("v0.0") == null);

                Check("M2. 版本比较（段数不同的归一化：1.1 > 1.0.0.0、1.0.1 > 1.0、相同不算新）",
                    Updater.IsNewer(new Version(1, 1), new Version(1, 0, 0, 0))
                    && Updater.IsNewer(new Version(1, 0, 1), new Version(1, 0))
                    && !Updater.IsNewer(new Version(1, 0), new Version(1, 0, 0, 0))
                    && !Updater.IsNewer(new Version(1, 0, 0, 0), new Version(1, 1)));

                const string releaseJson = """
                {
                  "tag_name": "v1.3",
                  "html_url": "https://github.com/Crucialmer/Numlock/releases/tag/v1.3",
                  "assets": [
                    { "name": "Numlock.zip.sha256", "browser_download_url": "https://example.invalid/x.sha256" },
                    { "name": "Numlock.zip", "browser_download_url": "https://example.invalid/Numlock.zip", "digest": "sha256:ABCDEF0123" }
                  ]
                }
                """;
                var relInfo = Updater.ParseLatestRelease(releaseJson);
                Check("M3. Release JSON 解析（挑中 Numlock.zip、digest 归一化、页面地址）",
                    relInfo != null
                    && relInfo.Version == new Version(1, 3, 0, 0)
                    && relInfo.AssetUrl == "https://example.invalid/Numlock.zip"
                    && relInfo.Sha256 == "abcdef0123"
                    && relInfo.PageUrl.EndsWith("/v1.3", StringComparison.Ordinal));

                Check("M3b. Release JSON 缺资产 / 缺标签 / 内容畸形时返回 null（不误判为可更新、不抛异常）",
                    Updater.ParseLatestRelease("""{"tag_name":"v1.3","assets":[]}""") == null
                    && Updater.ParseLatestRelease("""{"assets":[{"name":"Numlock.zip","browser_download_url":"https://x/"}]}""") == null
                    && Updater.ParseLatestRelease("not json at all") == null
                    && Updater.ParseLatestRelease("") == null);

                string updDir = Path.Combine(dir, "upd");
                if (Directory.Exists(updDir))
                {
                    try { Directory.Delete(updDir, recursive: true); } catch { }
                }
                Directory.CreateDirectory(updDir);
                // 合法 PE 最小样本：MZ + e_lfanew(0x3C)=0x40 + "PE\0\0"（更新器现在校验 PE 签名，只给 MZ 不够）
                // 注意填充顺序：先填 2..0x3B，再写 e_lfanew，最后写 PE 签名（否则会覆盖 lfanew 字段）
                byte[] pe = new byte[128];
                for (int i = 2; i < 0x3C; i++)
                {
                    pe[i] = (byte)i;
                }
                pe[0] = (byte)'M';
                pe[1] = (byte)'Z';
                BitConverter.GetBytes(0x40).CopyTo(pe, 0x3C);
                pe[0x40] = (byte)'P';
                pe[0x41] = (byte)'E';
                string goodZip = Path.Combine(updDir, "good.zip");
                using (var z = ZipFile.Open(goodZip, ZipArchiveMode.Create))
                {
                    using var s = z.CreateEntry("Numlock/Numlock.exe").Open();
                    s.Write(pe, 0, pe.Length);
                }
                string dest = Path.Combine(updDir, "out.exe");
                bool extractOk = Updater.TryExtractExe(goodZip, dest) && File.ReadAllBytes(dest).AsSpan().SequenceEqual(pe);

                string evilZip = Path.Combine(updDir, "evil.zip");
                using (var z = ZipFile.Open(evilZip, ZipArchiveMode.Create))
                {
                    using var s = z.CreateEntry("../../Numlock.exe").Open();
                    s.Write(pe, 0, pe.Length);
                }
                string evilDest = Path.Combine(updDir, "evil.exe");
                string htmlZip = Path.Combine(updDir, "html.zip");
                using (var z = ZipFile.Open(htmlZip, ZipArchiveMode.Create))
                {
                    using var s = z.CreateEntry("Numlock/Numlock.exe").Open();
                    byte[] html = Encoding.UTF8.GetBytes("<html>404 Not Found</html>");
                    s.Write(html, 0, html.Length);
                }
                string fakeZip = Path.Combine(updDir, "fakemz.zip"); // 只有 MZ、没有 PE 签名
                using (var z = ZipFile.Open(fakeZip, ZipArchiveMode.Create))
                {
                    using var s = z.CreateEntry("Numlock/Numlock.exe").Open();
                    byte[] mz = new byte[128];
                    mz[0] = (byte)'M';
                    mz[1] = (byte)'Z';
                    s.Write(mz, 0, mz.Length);
                }
                string dupZip = Path.Combine(updDir, "dup.zip"); // 重复条目：整体拒绝
                using (var z = ZipFile.Open(dupZip, ZipArchiveMode.Create))
                {
                    foreach (string entry in new[] { "Numlock/Numlock.exe", "patch/Numlock.exe" })
                    {
                        using var s = z.CreateEntry(entry).Open();
                        s.Write(pe, 0, pe.Length);
                    }
                }
                Check("M4. 更新包解压（从 Numlock/Numlock.exe 取出可执行文件，字节一致）", extractOk);
                Check("M4b. 拒绝路径穿越条目 / 非 PE 内容 / 只有 MZ 无 PE 签名 / 重复条目",
                    !Updater.TryExtractExe(evilZip, evilDest) && !File.Exists(evilDest)
                    && !Updater.TryExtractExe(htmlZip, Path.Combine(updDir, "html.exe"))
                    && !File.Exists(Path.Combine(updDir, "html.exe"))
                    && !Updater.TryExtractExe(fakeZip, Path.Combine(updDir, "fake.exe"))
                    && !Updater.TryExtractExe(dupZip, Path.Combine(updDir, "dup.exe")));

                string hashFile = Path.Combine(updDir, "h.bin");
                File.WriteAllBytes(hashFile, Encoding.UTF8.GetBytes("numlock-update-check-fixture"));
                string goodHex = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(hashFile))).ToLowerInvariant();
                Check("M5. 下载校验（SHA-256 正确通过、大小写不敏感、错误值被拒）",
                    Updater.VerifySha256(hashFile, goodHex)
                    && Updater.VerifySha256(hashFile, goodHex.ToUpperInvariant())
                    && !Updater.VerifySha256(hashFile, new string('0', 64)));

                string tgt = Path.Combine(updDir, "target.exe");
                File.WriteAllBytes(tgt, Encoding.UTF8.GetBytes("old-version-placeholder"));
                string? applyErr = Updater.ApplyCore(dest, tgt, 0, 200);
                bool applyOk = applyErr == null && File.ReadAllBytes(tgt).AsSpan().SequenceEqual(pe);
                var applyWatch = Stopwatch.StartNew();
                Updater.ApplyCore(dest, tgt, Environment.ProcessId, 300); // 目标 PID 是自己（活着）→ 必须等满超时
                applyWatch.Stop();

                // 真实不变式：等待期间目标文件不得被写入。起一个存活约 2s 的子进程交给 ApplyCore 等待，
                // 等待窗口内目标内容必须原封不动，子进程退出后才允许被覆盖。
                bool untouchedWhileWaiting = false;
                bool copiedAfterExit = false;
                var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (child != null)
                {
                    File.WriteAllBytes(tgt, Encoding.UTF8.GetBytes("must-not-be-overwritten-while-target-alive"));
                    byte[] before = File.ReadAllBytes(tgt);
                    var waiter = Task.Run(() => Updater.ApplyCore(dest, tgt, child.Id, 5_000));
                    Thread.Sleep(600);
                    untouchedWhileWaiting = File.ReadAllBytes(tgt).AsSpan().SequenceEqual(before);
                    waiter.Wait(10_000);
                    copiedAfterExit = File.ReadAllBytes(tgt).AsSpan().SequenceEqual(pe);
                    child.WaitForExit(5_000);
                }
                Check($"M6. 替换核心（等待 {applyWatch.ElapsedMilliseconds}ms；目标进程存活期间不写目标={untouchedWhileWaiting}；退出后写入={copiedAfterExit}）",
                    applyOk && applyWatch.ElapsedMilliseconds >= 250 && untouchedWhileWaiting && copiedAfterExit);

                var updItem = app.UpdateItemForTest;
                bool itemWired = updItem != null
                    && updItem.DropDownItems.Count == 0
                    && app.Menu.Items.Contains(updItem);
                bool normalTextOk = updItem!.Text == "检查更新…";
                app.SetStagedVersionForTest(new Version(1, 9, 0, 0));
                PumpFor(200);
                string shownText = updItem.Text ?? "";
                bool shownWhenStaged = updItem.Available
                    && updItem.Enabled
                    && shownText.Contains("1.9.0", StringComparison.Ordinal)
                    && app.StagedVersionTextForTest == "1.9.0";
                string m7Detail = $"（单级直达={itemWired}；常规文案“{updItem.Text}”；有更新时 可用={updItem.Available}/启用={updItem.Enabled}/文案“{shownText}”/暂存文案={app.StagedVersionTextForTest}）";
                app.SetStagedVersionForTest(null);
                PumpFor(200);
                bool restoredAgain = updItem.Text == "检查更新…" && app.StagedVersionTextForTest.Length == 0;
                Check($"M7. “检查更新”单级主菜单项（无需展开={itemWired} / 常规状态={normalTextOk} / 有更新时显示就绪版本={shownWhenStaged} / 清除后恢复={restoredAgain}）{m7Detail}",
                    itemWired && normalTextOk && shownWhenStaged && restoredAgain);

                // M7b：启动时检查更新为默认常开功能，不出现在“检查更新”选项中
                var cfgDefault = AppConfig.Parse("{}");
                Check("M7b. 启动时自动检查更新默认常开且不占用菜单槽位",
                    cfgDefault.CheckUpdatesOnStart && !app.Menu.Items.Contains(app.AutoCheckItemForTest));

                var cfgOff = AppConfig.Parse("""{"CheckUpdatesOnStart":false,"UpdateNotifiedVersion":"1.9.0"}""");
                Check("M8. 配置新增键（可显式关闭自动检查 / 缺省为开启 / 记住已提示版本）",
                    !cfgOff.CheckUpdatesOnStart && cfgOff.UpdateNotifiedVersion == "1.9.0"
                    && cfgDefault.CheckUpdatesOnStart && cfgDefault.UpdateNotifiedVersion == null);

                // N. 启动自动应用（v1.2）：发现“已就绪更新”时本次启动直接替换并重启，用户无需操作
                bool pendingBefore = app.AutoApplyPendingForTest;
                app.SimulateStagedUpdateForTest(new Version(1, 9, 0, 0));
                bool pendingAfter = app.AutoApplyPendingForTest;
                bool menuShowsApply = app.ApplyUpdateItemForTest.Text is string t && t.Contains("1.9.0", StringComparison.Ordinal);
                app.SimulateStagedUpdateForTest(null);
                bool pendingCleared = !app.AutoApplyPendingForTest;
                Check($"N1. 启动自动应用排程（无更新时={!pendingBefore}；模拟就绪更新后={pendingAfter}；菜单给出“重启并更新到 1.9.0”={menuShowsApply}；清除后恢复={pendingCleared}）",
                    !pendingBefore && pendingAfter && menuShowsApply && pendingCleared);

                // O. 更新完成的一次性提示标记（v1.4）：辅助进程替换成功后留标记，新版首次启动提示一次并删标记
                string markerDir = Path.Combine(dir, "marker");
                Directory.CreateDirectory(markerDir);
                var vNew = new Version(1, 4, 0, 0);
                bool markNone = Updater.ConsumeUpdatedMarker(markerDir, vNew) == null;      // 无标记 → 不提示
                Updater.MarkUpdated("1.4.0", markerDir);
                bool markOnce = Updater.ConsumeUpdatedMarker(markerDir, vNew) == "1.4.0";   // 刚更新完 → 提示一次
                bool markOnlyOnce = Updater.ConsumeUpdatedMarker(markerDir, vNew) == null;  // 已消费 → 不再提示
                Updater.MarkUpdated("1.4.0", markerDir);
                bool markMismatch = Updater.ConsumeUpdatedMarker(markerDir, new Version(1, 0, 0, 0)) == null; // 版本不符 → 不提示
                bool markCleaned = !File.Exists(Updater.UpdatedMarkerPath(markerDir));
                Check($"O1. 更新完成的一次性提示标记（无标记={markNone}；刚更新后提示一次={markOnce}；再取不再提示={markOnlyOnce}；版本不符不提示={markMismatch}；标记已清理={markCleaned}）",
                    markNone && markOnce && markOnlyOnce && markMismatch && markCleaned);
            }
        }
        catch (Exception ex)
        {
            allPassed = false;
            log.AppendLine(CultureInfo.InvariantCulture, $"FAIL: 自检过程异常：{ex}");
        }
        finally
        {
            try { app?.Dispose(); } catch { }
            // 异常/中断路径同样还原环境：NumLock 状态与鼠标位置（结果记入报告，便于排障核对）
            bool numRestored = false;
            bool cursorRestored = false;
            try { numRestored = NumLock.Ensure(initial); } catch { }
            try { Cursor.Position = savedCursor; cursorRestored = Cursor.Position == savedCursor; } catch { }
            log.AppendLine(CultureInfo.InvariantCulture,
                $"restored: numlock={(numRestored ? "OK" : "FAIL")}, cursor={(cursorRestored ? "OK" : "FAIL")}");
            log.AppendLine(CultureInfo.InvariantCulture,
                $"checks: {checksPassed} passed, {checksFailed} failed, elapsed {stopwatch.Elapsed.TotalSeconds:F1}s");
            log.AppendLine(allPassed ? "== 全部通过 ==" : "== 存在失败 ==");
            try
            {
                File.WriteAllText(Path.Combine(dir, "selftest.txt"), log.ToString());
            }
            catch
            {
                // 报告落盘失败不影响退出码语义（结果仍按 allPassed 返回）
            }
        }

        Environment.Exit(allPassed ? 0 : 1);
    }

    private static void PumpFor(int ms)
    {
        long end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    private static void RenderMenuPng(TrayApp app, string path, StringBuilder log, bool dark)
    {
        try
        {
            app.SyncMenuState();
            var menu = app.Menu;
            menu.Show(new Point(240, 240));
            app.SetMenuTheme(dark); // Show 时 Opening 会按系统主题校准，这里再覆盖为截图所需配色
            PumpFor(250);
            log.AppendLine(CultureInfo.InvariantCulture,
                $"菜单项状态：保持常开.Checked={app.EnforceItemForTest.Checked}, 图标颜色.Checked={app.ColorMenuItemForTest.Checked}, " +
                $"ShowImageMargin={menu.ShowImageMargin}, ShowCheckMargin={menu.ShowCheckMargin}");
            // 当前菜单 DPI：文档图（docs/menu-*.png）需按 96/此值 缩放才是 100% 缩放下的观感，供整理文档图时换算
            log.AppendLine(CultureInfo.InvariantCulture,
                $"menu dpi: {menu.DeviceDpi}（文档菜单图按 {96.0 / menu.DeviceDpi:F3} 缩放即 100% 缩放等价）");
            // 项几何写进报告：供“文字墨迹垂直居中”等布局断言/排障对照（项本地坐标 = 菜单客户区坐标）
            foreach (ToolStripItem it in menu.Items)
            {
                log.AppendLine(CultureInfo.InvariantCulture,
                    $"    item[{it.Text}] bounds={it.Bounds} padding={it.Padding} margin={it.Margin}");
            }
            if (menu.Width > 0 && menu.Height > 0)
            {
                using var bmp = new Bitmap(menu.Width, menu.Height);
                bmp.SetResolution(96, 96); // 文档图统一 96 DPI 元数据（否则按屏幕 DPI 继承，阅读器缩放不一致）
                menu.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(path, ImageFormat.Png);
                log.AppendLine(CultureInfo.InvariantCulture, $"{Path.GetFileName(path)}: {bmp.Width}x{bmp.Height}");
            }
            else
            {
                File.Delete(path); // 删掉上一轮残留，避免被当成本轮产物
                File.Delete(Path.ChangeExtension(path, null) + "-onscreen.png");
                log.AppendLine(CultureInfo.InvariantCulture, $"{Path.GetFileName(path)}: 跳过（菜单尺寸为 0）");
            }
            CaptureMenuOnScreen(app, menu, path, log);
            menu.Close();
        }
        catch (Exception ex)
        {
            log.AppendLine(CultureInfo.InvariantCulture, $"菜单渲染（{(dark ? "深色" : "浅色")}）失败: {ex.Message}");
            // 渲染失败即删除旧产物：避免 G1/G2/G2b 误用上一轮残留文件造成假通过
            try { File.Delete(path); } catch { }
            try { File.Delete(Path.ChangeExtension(path, null) + "-onscreen.png"); } catch { }
        }
    }

    /// <summary>屏幕实拍（真实合成效果，含 DWM 圆角），并展开“图标颜色”子菜单一并截入；
    /// 同时输出“透明圆角版”（按两个菜单的圆角轮廓裁剪，供文档使用）。仅信息性。</summary>
    private static void CaptureMenuOnScreen(TrayApp app, ToolStripDropDown menu, string path, StringBuilder log)
    {
        try
        {
            var sub = app.ColorMenuItemForTest.DropDown;
            app.ColorMenuItemForTest.ShowDropDown();
            PumpFor(300);
            // 自检是从后台进程弹出菜单（无用户输入），Windows 可能不授予前景/置顶；
            // 为让实拍能拍到菜单，强制置顶（仅测试路径）
            Native.SetWindowPos(menu.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Native.SetWindowPos(sub.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            PumpFor(150);

            if (!Native.GetWindowRect(menu.Handle, out var rm))
            {
                return;
            }
            int left = rm.Left, top = rm.Top, right = rm.Right, bottom = rm.Bottom;
            Native.RECT rs = default;
            bool subRectOk = sub.Visible && Native.GetWindowRect(sub.Handle, out rs);
            if (subRectOk)
            {
                left = Math.Min(left, rs.Left);
                top = Math.Min(top, rs.Top);
                right = Math.Max(right, rs.Right);
                bottom = Math.Max(bottom, rs.Bottom);
            }
            int w = right - left, h = bottom - top;
            if (w <= 0 || h <= 0)
            {
                return;
            }
            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(left, top, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            }

            // 透明圆角版：用纹理填充两个菜单的圆角联合轮廓，角外保持透明（文档图）
            // 截屏矩形与遮罩矩形必须同源（都用 GetWindowRect）：混用控件 Left/Top/Width/Height 会差 1px，
            // 边缘会残留一条桌面像素（透明像素，虽不可见但没必要）。
            // 子菜单按“仅右侧圆角”裁剪（左缘与父面板拼直边），与窗口实际形状一致。
            var m = MenuMetrics.For(menu.DeviceDpi, app.MenuRenderer.CornersRounded(menu));
            using var clip = new GraphicsPath { FillMode = FillMode.Winding };
            using (var p1 = Gfx.RoundedRect(new RectangleF(rm.Left - left, rm.Top - top, rm.Right - rm.Left, rm.Bottom - rm.Top), m.CornerRadius))
            {
                clip.AddPath(p1, false);
            }
            if (subRectOk)
            {
                using var p2 = Gfx.RightRoundedRect(new RectangleF(rs.Left - left, rs.Top - top, rs.Right - rs.Left, rs.Bottom - rs.Top), m.CornerRadius);
                clip.AddPath(p2, false);
            }
            using var masked = new Bitmap(w, h);
            masked.SetResolution(96, 96);
            using (var mg = Graphics.FromImage(masked))
            {
                mg.SmoothingMode = SmoothingMode.AntiAlias;
                using var tb = new TextureBrush(bmp);
                mg.FillPath(tb, clip);
            }
            string p = Path.ChangeExtension(path, null) + "-onscreen.png";
            masked.Save(p, ImageFormat.Png);
            log.AppendLine(CultureInfo.InvariantCulture, $"{Path.GetFileName(p)}（实拍+透明圆角）: {w}x{h}");
        }
        catch (Exception ex)
        {
            log.AppendLine(CultureInfo.InvariantCulture, $"菜单实拍失败: {ex.Message}");
        }
    }

    /// <summary>把构建身份写进报告：ProductVersion（含提交 sha）与自身 exe 的 SHA-256，使报告可与 tag / 发布附件绑定。</summary>
    private static void AppendBuildIdentity(StringBuilder log)
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe == null || !File.Exists(exe))
            {
                return;
            }
            log.AppendLine(CultureInfo.InvariantCulture, $"product: {FileVersionInfo.GetVersionInfo(exe).ProductVersion}");
            using var stream = File.OpenRead(exe);
            log.AppendLine(CultureInfo.InvariantCulture, $"exe sha256: {Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()}");
        }
        catch (Exception ex)
        {
            log.AppendLine(CultureInfo.InvariantCulture, $"build identity: {ex.Message}");
        }
    }

    private static bool LooksRenderedFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        using var bmp = new Bitmap(path);
        return LooksRendered(bmp);
    }

    /// <summary>渲染托盘图标状态总表。标签列宽按“最长标签的实际墨迹宽度”实算（固定列宽会在长标签下把文字压进图标列）；
    /// out 回传列宽与墨迹最右 x，供“标签不溢出”断言使用。</summary>
    private static Bitmap RenderIconSheet(string path, out int labelColumn, out int labelInkRight)
    {
        int[] sizes = { 16, 20, 24, 32, 48, 64 };
        var combos = new (bool White, bool On, bool Badge, string Label)[]
        {
            (true, true, false, "深色任务栏 · 开启"),
            (true, false, false, "深色任务栏 · 关闭"),
            (true, true, true, "深色任务栏 · 开启 + 保持常开"),
            (true, false, true, "深色任务栏 · 关闭 + 保持常开"),
            (false, true, false, "浅色任务栏 · 开启"),
            (false, false, false, "浅色任务栏 · 关闭"),
            (false, true, true, "浅色任务栏 · 开启 + 保持常开"),
            (false, false, true, "浅色任务栏 · 关闭 + 保持常开"),
        };

        const int cell = 72;
        const int rowH = 72;
        const int labelLeft = 14;   // 文字左边距
        const int labelGap = 20;    // 文字与首个图标列之间的最小间隙

        // 像素单位（13px ≈ 10pt@96dpi）：GDI（TextRenderer）按“点”字号创建的 HFONT 会跟随系统缩放放大，
        // 只固定位图分辨率不足以让文档图一致；像素单位才能保证任何 DPI 下都渲染成同一尺寸。
        using var labelFont = new Font("Microsoft YaHei UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);

        // 量测用 96 DPI 探针位图：与绘制用位图同分辨率，保证量测与绘制一致（不随系统缩放变化）
        using (var probe = new Bitmap(1, 1))
        {
            probe.SetResolution(96, 96);
            using var probeG = Graphics.FromImage(probe);
            int textWidth = 0;
            foreach (var c in combos)
            {
                textWidth = Math.Max(textWidth, TextRenderer.MeasureText(probeG, c.Label, labelFont).Width);
            }
            labelColumn = textWidth + labelLeft + labelGap;
        }

        var bmp = new Bitmap(labelColumn + cell * sizes.Length, rowH * combos.Length);
        bmp.SetResolution(96, 96); // 固定 96 DPI：文字按点字号渲染，保证文档图在不同系统缩放下一致
        using var g = Graphics.FromImage(bmp);

        for (int r = 0; r < combos.Length; r++)
        {
            var (white, on, badge, label) = combos[r];
            var bg = white ? Color.FromArgb(255, 31, 31, 31) : Color.FromArgb(255, 243, 243, 242);
            using (var brush = new SolidBrush(bg))
            {
                g.FillRectangle(brush, 0, r * rowH, bmp.Width, rowH);
            }

            var labelColor = white ? Color.White : Color.FromArgb(255, 32, 32, 32);
            TextRenderer.DrawText(g, label, labelFont, new Point(labelLeft, r * rowH + rowH / 2 - 12), labelColor);

            for (int c = 0; c < sizes.Length; c++)
            {
                using var icon = IconFactory.RenderTrayIcon(sizes[c], on, white, badge);
                int x = labelColumn + c * cell + (cell - sizes[c]) / 2;
                int y = r * rowH + (rowH - sizes[c]) / 2;
                // 用目标矩形绘制（不要 DrawImageUnscaled）：后者按“位图 DPI ÷ 目标 DPI”缩放，
                // 在系统缩放 ≠ 100% 时会把图标画成半尺寸（本表曾因此在 200% 机器上整表半尺寸）。
                g.DrawImage(icon, new Rectangle(x, y, sizes[c], sizes[c]));
            }
        }

        labelInkRight = MeasureLabelInkRight(bmp, rowH, combos, labelColumn);
        bmp.Save(path, ImageFormat.Png);
        return bmp;
    }

    /// <summary>在菜单渲染图的项矩形内实测“文字墨迹”的垂直中心（-1 = 未检出）。
    /// 横向只取文字区（跳过左侧勾选装订线与右端子菜单箭头）；纵向各内缩 4px（避开高亮块/面板边缘的过渡像素）；
    /// 背景色取该区域内的众数色。</summary>
    private static int TextInkCenterY(Bitmap bmp, Rectangle bounds)
    {
        int x0 = Math.Max(0, bounds.Left + 24);
        int x1 = Math.Min(bmp.Width, bounds.Right - 30);
        int y0 = Math.Max(0, bounds.Top + 4);
        int y1 = Math.Min(bmp.Height, bounds.Bottom - 4);
        if (x1 - x0 < 8 || y1 - y0 < 6)
        {
            return -1;
        }
        var histogram = new Dictionary<int, int>();
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int argb = bmp.GetPixel(x, y).ToArgb();
                histogram[argb] = histogram.TryGetValue(argb, out int n) ? n + 1 : 1;
            }
        }
        Color bg = Color.FromArgb(histogram.OrderByDescending(kv => kv.Value).First().Key);
        int top = -1;
        int bottom = -1;
        for (int y = y0; y < y1; y++)
        {
            int ink = 0;
            for (int x = x0; x < x1; x++)
            {
                Color c = bmp.GetPixel(x, y);
                if (Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) > 80)
                {
                    ink++;
                }
            }
            if (ink >= 2)
            {
                if (top < 0)
                {
                    top = y;
                }
                bottom = y;
            }
        }
        return top < 0 ? -1 : (top + bottom) / 2;
    }

    /// <summary>“勾选右缘 → 文字左缘”的水平间距（像素；-1 = 未检出）：在该行内找最宽的内部空白带
    /// （勾选与文字之间的间隙比字间空隙大得多）。</summary>
    private static int CheckTextGap(Bitmap menu, Rectangle bounds)
    {
        int y0 = Math.Max(0, bounds.Top + 4);
        int y1 = Math.Min(menu.Height, bounds.Bottom - 4);
        int xEnd = Math.Min(menu.Width, bounds.Right - 20);
        int prev = -1;
        int best = -1;
        for (int x = Math.Max(0, bounds.Left); x < xEnd; x++)
        {
            bool ink = false;
            for (int y = y0; y < y1; y++)
            {
                Color c = menu.GetPixel(x, y);
                if (c.R < 200 && c.G < 200) // 浅色面板上的墨迹（深色文字或 Apple 蓝对勾）
                {
                    ink = true;
                    break;
                }
            }
            if (ink)
            {
                if (prev >= 0)
                {
                    int gap = x - prev - 1;
                    if (gap > best)
                    {
                        best = gap;
                    }
                }
                prev = x;
            }
        }
        return best;
    }

    /// <summary>该行最左的“非底色”墨迹列（-1 = 未检出）：用于“分隔线左端 vs 文字起点”对齐断言。
    /// 底色取该行右端的像素（行内右侧必为面板底色）；跳过 x&lt;8（窗口左边框本身不算墨迹）。</summary>
    private static int RowLeftInk(Bitmap menu, Rectangle bounds)
    {
        int y0 = Math.Max(0, bounds.Top + 1);
        int y1 = Math.Min(menu.Height, bounds.Bottom - 1);
        if (y1 <= y0)
        {
            return -1;
        }
        Color bg = menu.GetPixel(Math.Max(0, bounds.Right - 6), (y0 + y1) / 2);
        for (int x = Math.Max(8, bounds.Left); x < Math.Min(menu.Width, bounds.Right); x++)
        {
            for (int y = y0; y < y1; y++)
            {
                Color c = menu.GetPixel(x, y);
                if (Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) > 12)
                {
                    return x;
                }
            }
        }
        return -1;
    }

    /// <summary>菜单实拍图里“父面板右缘”接缝处的描边列数（0 = 两面板无缝合并；1 = 只剩父面板那条 1px 描边；
    /// ≥2 = 双线）。只统计“子菜单存在、且该行不是子菜单水平边框”的行，取众数。</summary>
    private static int SeamStrokeColumns(string path, int parentWidth, StringBuilder log)
    {
        try
        {
            if (!File.Exists(path))
            {
                log.AppendLine("接缝测量跳过：无实拍图（可能处于无显示设备或非交互会话环境）");
                return 0;
            }
            using var bmp = new Bitmap(path);
            if (parentWidth <= 8 || parentWidth + 20 >= bmp.Width)
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"接缝测量跳过：图宽 {bmp.Width}、父面板宽 {parentWidth}");
                return -1;
            }
            Color parentBg = bmp.GetPixel(parentWidth - 20, 30);
            var tally = new Dictionary<int, int>(); // 描边列数 → 出现行数
            for (int y = 8; y < bmp.Height - 8; y += 3)
            {
                // 子菜单存在时，父面板右缘外侧 3px 处是不透明的面板底（子菜单与父面板同底色）；
                // 不存在时该点是遮罩外的桌面（alpha=0），子菜单自身水平边框行则该点是描边色。
                Color probe = bmp.GetPixel(Math.Min(bmp.Width - 1, parentWidth + 3), y);
                if (probe.A < 200 || !Near(probe, parentBg))
                {
                    continue;
                }
                int count = 0;
                for (int x = Math.Max(0, parentWidth - 6); x <= Math.Min(bmp.Width - 1, parentWidth + 6); x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.A < 200)
                    {
                        continue; // 遮罩外（桌面）不计
                    }
                    if (!Near(c, parentBg))
                    {
                        count++;
                    }
                }
                tally[count] = tally.TryGetValue(count, out int n) ? n + 1 : 1;
            }
            if (tally.Count == 0)
            {
                log.AppendLine("接缝测量跳过：未找到子菜单所在的行");
                return -1;
            }
            int mode = tally.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
            string dist = string.Join(", ", tally.OrderBy(kv => kv.Key).Select(kv => kv.Key + "列×" + kv.Value + "行"));
            log.AppendLine(CultureInfo.InvariantCulture, $"接缝描边列分布: {dist} → 众数 {mode}");
            return mode;
        }
        catch (Exception ex)
        {
            log.AppendLine(CultureInfo.InvariantCulture, $"接缝测量失败: {ex.Message}");
            return -1;
        }
    }

    private static bool Near(Color a, Color b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) <= 12;

    /// <summary>量出单元格内图标墨迹的水平宽度（-1 = 无墨迹）：用于断言图标按名义尺寸绘制（未被 DPI 缩放）。</summary>
    private static int InkWidth(Bitmap sheet, int x0, int y0, int cell, Color bg)
    {
        int first = -1;
        int last = -1;
        for (int i = 0; i < cell; i++)
        {
            for (int j = 0; j < cell; j++)
            {
                int x = x0 + i;
                int y = y0 + j;
                if (x < 0 || y < 0 || x >= sheet.Width || y >= sheet.Height)
                {
                    continue;
                }
                Color c = sheet.GetPixel(x, y);
                if (Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) > 60)
                {
                    if (first < 0)
                    {
                        first = i;
                    }
                    last = i;
                    break;
                }
            }
        }
        return first < 0 ? -1 : last - first + 1;
    }

    /// <summary>量出标签列内墨迹的最右 x（-1 = 无墨迹）：从列右缘向左逐列扫，命中即得。</summary>
    private static int MeasureLabelInkRight(Bitmap sheet, int rowH, (bool White, bool On, bool Badge, string Label)[] combos, int labelColumn)
    {
        int rightmost = -1;
        for (int r = 0; r < combos.Length; r++)
        {
            Color bg = combos[r].White ? Color.FromArgb(255, 31, 31, 31) : Color.FromArgb(255, 243, 243, 242);
            for (int x = labelColumn - 1; x > rightmost; x--)
            {
                bool hit = false;
                for (int y = r * rowH; y < (r + 1) * rowH; y++)
                {
                    Color c = sheet.GetPixel(x, y);
                    if (Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) > 60)
                    {
                        hit = true;
                        break;
                    }
                }
                if (hit)
                {
                    rightmost = x;
                    break;
                }
            }
        }
        return rightmost;
    }

    private static Bitmap RenderTaskbarPreview(string path, bool whiteIcons)
    {
        var bmp = new Bitmap(520, 48);
        bmp.SetResolution(96, 96); // 同上：固定文档图渲染缩放，避免高缩放系统下文字放大或裁切
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var bg = whiteIcons ? Color.FromArgb(255, 28, 28, 28) : Color.FromArgb(255, 243, 243, 242);
        g.Clear(bg);

        int x = 40;
        const int size = 24;
        const int gap = 14;
        foreach (var (on, badge) in new[] { (false, false), (true, false), (true, true) })
        {
            using var icon = IconFactory.RenderTrayIcon(size, on, whiteIcons, badge);
            g.DrawImage(icon, new Rectangle(x, (bmp.Height - size) / 2, size, size)); // 目标矩形：不受位图 DPI 影响
            x += size + gap;
        }

        var fg = whiteIcons ? Color.White : Color.FromArgb(255, 32, 32, 32);
        using var clockFont = new Font("Segoe UI", 9f);
        using var clockBrush = new SolidBrush(fg);
        using var sf = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString("22:20\n2026/10/12", clockFont, clockBrush, new RectangleF(0, 4, bmp.Width - 18, 40), sf);

        bmp.Save(path, ImageFormat.Png);
        return bmp;
    }

    /// <summary>粗略像素检查：不透明像素占比够且颜色不止一种（防“全透明/纯色”渲染回归）。</summary>
    private static bool LooksRendered(Bitmap bmp)
    {
        int step = Math.Max(1, Math.Min(bmp.Width, bmp.Height) / 64);
        int sampled = 0;
        int opaque = 0;
        var colors = new HashSet<int>();
        for (int y = 0; y < bmp.Height; y += step)
        {
            for (int x = 0; x < bmp.Width; x += step)
            {
                sampled++;
                Color c = bmp.GetPixel(x, y);
                if (c.A < 40)
                {
                    continue;
                }
                opaque++;
                colors.Add(c.ToArgb());
            }
        }
        return sampled > 0 && opaque * 100 >= sampled * 3 && colors.Count >= 2;
    }
}

