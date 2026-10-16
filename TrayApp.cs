using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Numlock;

/// <summary>托盘应用主体：托盘图标 + 右键菜单 + 状态看护（保持常开）。</summary>
internal sealed class TrayApp : ApplicationContext
{
    private const int TimerIntervalMs = 300;
    private const int HookRebuildIntervalMs = 60_000; // 钩子周期重挂：自愈被系统静默摘除的钩子

    internal AppConfig Config { get; }
    internal NotifyIcon Tray { get; }
    internal ContextMenuStrip Menu { get; }
    internal bool HookInstalled => _hookGuard.HookInstalled;

    /// <summary>macOS 风格菜单渲染器（菜单与子菜单共用）。</summary>
    internal MacMenuRenderer MenuRenderer { get; }

    /// <summary>自检用：主菜单“切换 NumLock”项。</summary>
    internal ToolStripMenuItem ToggleItemForTest => _toggleItem;

    /// <summary>自检用：“保持常开”项。</summary>
    internal ToolStripMenuItem EnforceItemForTest => _enforceItem;

    /// <summary>自检用：“图标颜色”子菜单父项。</summary>
    internal ToolStripMenuItem ColorMenuItemForTest => _colorMenu;

    private readonly MacBrandHeaderItem _brandHeader;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _enforceItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _modeAuto;
    private readonly ToolStripMenuItem _modeWhite;
    private readonly ToolStripMenuItem _modeBlack;
    private readonly ToolStripMenuItem _colorMenu;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripMenuItem _autoCheckItem;
    private readonly System.Windows.Forms.Timer _timer;

    private Icon? _currentIcon;
    private bool _lastOn;
    private bool _lastEnforce;
    private bool _lastWhite;
    private int _lastIconSize;                  // 上次渲染用的托盘图标边长（系统尺寸变化后须重绘）
    private volatile bool _themeRefresh;        // 系统主题变化后请求重绘（仅自动模式）
    private volatile bool _displayRefresh;      // 分辨率/DPI 变化后请求重绘（尺寸可能改变）
    private volatile bool _showRunningBalloon;  // 第二次启动时提示“已在运行”
    private ConfigLoadStatus _configLoadStatus; // 配置读取结果：损坏/读失败时提示一次
    private bool _configBackupPending;          // 启动时配置未能读取：首次保存前先尽力留底
    private bool _enforceFailureNotified;       // 看护失败只提示一次（成功后退避复位）
    private bool _shuttingDown;
    private readonly KeyboardHookGuard _hookGuard;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _ackEvent;      // 首实例确认“已在运行”后 Set，第二实例据此安静退出
    private Thread? _showEventThread;
    private volatile bool _stopShowThread;

    // —— 自动更新：后台线程只写 volatile 字段，UI 线程在看护 tick 里读取并处理（不跨线程碰控件） ——
    private const int UpdateOutcomeNone = 0;
    private const int UpdateOutcomeUpToDate = 1;
    private const int UpdateOutcomeStaged = 2;        // 新版本已下载好，退出时自动替换
    private const int UpdateOutcomeFailed = 3;        // 网络/解析失败
    private const int UpdateOutcomeStageFailed = 4;   // 发现新版本但自动下载失败
    private const int UpdateOutcomeAvailable = 5;     // 发现新版本，等待用户确认下载
    private volatile bool _updateCheckRequested;
    private volatile bool _updateManualRequest;
    private volatile bool _updateDone;
    private volatile int _updateOutcome;
    private volatile bool _updateManualResult;
    private volatile string _updateVersionText = "";
    private volatile bool _updateBusy;
    private volatile bool _downloadBusy;
    private UpdateInfo? _latestUpdateInfo;
    private CancellationTokenSource? _downloadCts;
    private Version? _stagedVersion;          // 已就绪的更新（同目录 Numlock.exe.new）的版本
    private bool _autoApplyPending;           // 启动时发现已就绪更新：本次启动直接用新版替换并重启（用户无需操作）
    private bool _applyHelperSpawned;         // 已拉起更新辅助进程：退出路径据此去重，避免同一次更新拉起两个辅助进程
    private string _pendingUpdateNotice = ""; // 启动时发现“已就绪更新”但无法自动应用时：在首个看护周期提示一次
    private string _pendingUpdatedNotice = "";// 刚完成更新（辅助进程留下的标记）：在首个看护周期提示一次“已更新到 X”
    private long _nextAutoCheckMs;            // 下次自动检查时刻（启动后 12s 首查，之后每 24h）

    /// <summary>已就绪更新的展示文案（无则为空）。</summary>
    private string StagedText => _stagedVersion?.ToString(3) ?? "";

    internal TrayApp(EventWaitHandle? showEvent = null, EventWaitHandle? ackEvent = null)
    {
        _ackEvent = ackEvent;
        Config = AppConfig.Load(out _configLoadStatus);
        _configBackupPending = _configLoadStatus == ConfigLoadStatus.ReadError;
        _hookGuard = new KeyboardHookGuard(() => Config.EnforceNumLock, ShowBalloon);

        // 菜单与渲染器先建：项的度量（内边距/边距）必须在加入菜单之前设好，
        // 这样菜单的自适应尺寸会一次算对（加进去之后再改会留下过时宽度）。
        // 注意：ToolStrip 菜单的宽度只按“文本”计算，不吃项的水平 Padding/边距，
        // 因此横向留白改由渲染器自绘（勾选装订线 = 系统图标栏区域）。
        MenuRenderer = new MacMenuRenderer { Dark = !SystemTheme.UsesLightTaskbar() };
        Menu = new MacContextMenuStrip(MenuRenderer);
        var mm = MenuMetrics.For(Menu.DeviceDpi);
        Menu.Font = CreateMenuFont(mm.Scale);

        void StyleItems(IList<ToolStripItem> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                ApplyItemMetrics(list[i], i, list.Count, mm);
            }
        }

        // 菜单不用系统原生 ToolTip：自绘 macOS 风格面板里冒出 Windows 原生气泡会破坏观感一致性
        // （macOS 菜单本身也没有悬停提示）；两项说明文案都收在“关于”里。
        _toggleItem = new ToolStripMenuItem("切换 NumLock");
        _toggleItem.Click += (_, _) => ToggleByUser();

        _enforceItem = new ToolStripMenuItem("保持常开");
        _enforceItem.Click += (_, _) => SetEnforce(!Config.EnforceNumLock);

        _autoStartItem = new ToolStripMenuItem("开机自动启动");
        _autoStartItem.Click += (_, _) => ToggleAutoStart();

        _modeAuto = new ToolStripMenuItem("自动（跟随系统）", null, (_, _) => SetIconColorMode(IconColor.Auto));
        _modeWhite = new ToolStripMenuItem("白色图标（深色任务栏）", null, (_, _) => SetIconColorMode(IconColor.White));
        _modeBlack = new ToolStripMenuItem("黑色图标（浅色任务栏）", null, (_, _) => SetIconColorMode(IconColor.Black));
        var colorMenu = new ToolStripMenuItem("图标颜色");
        _colorMenu = colorMenu;
        if (colorMenu.DropDown is ToolStripDropDownMenu dropDownMenu)
        {
            dropDownMenu.ShowImageMargin = false;
            dropDownMenu.ShowCheckMargin = true;
        }
        colorMenu.DropDown.Padding = new Padding(4, 6, 4, 6);
        colorMenu.DropDown.Font = CreateMenuFont(mm.Scale);
        StyleItems(new ToolStripItem[] { _modeAuto, _modeWhite, _modeBlack });
        colorMenu.DropDownItems.AddRange(new ToolStripItem[] { _modeAuto, _modeWhite, _modeBlack });

        var aboutItem = new ToolStripMenuItem("关于软件", null, (_, _) => ShowAbout());
        var exitItem = new ToolStripMenuItem("退出", null, (_, _) => ExitApp());

        // 单级直达更新项（不展开子菜单，无需“立即检查更新”项）
        _updateItem = new ToolStripMenuItem("检查更新…", null, (_, _) => OnUpdateMenuClicked());
        _autoCheckItem = new ToolStripMenuItem("启动时自动检查", null, (_, _) => ToggleAutoCheck());

        // macOS 原生 Menu Bar Extra 顶部专属品牌状态卡片：56px 高度精致状态行（不标版本号）
        _brandHeader = new MacBrandHeaderItem(() => MenuRenderer.Dark);
        _brandHeader.Click += (_, _) => ShowAbout();

        var mainItems = new ToolStripItem[]
        {
            _brandHeader,
            new ToolStripSeparator { Margin = new Padding(0, 4, 0, 4) },
            _toggleItem,
            new ToolStripSeparator { Margin = new Padding(0, 4, 0, 4) },
            _enforceItem,
            _autoStartItem,
            colorMenu,
            new ToolStripSeparator { Margin = new Padding(0, 4, 0, 4) },
            _updateItem,
            aboutItem,
            exitItem,
        };
        StyleItems(mainItems);
        Menu.Items.AddRange(mainItems);

        Menu.Opening += (_, _) =>
        {
            float s = Menu.DeviceDpi / 96f;
            Menu.MinimumSize = new Size((int)Math.Round(156 * s), 0);
            _brandHeader.Size = new Size((int)Math.Round(156 * s), (int)Math.Round(46 * s));
            _brandHeader.UpdateStatus(Config.EnforceNumLock, NumLock.IsOn);
            ApplyMenuMetrics(); // 显示器 DPI 变化时重算（常规情况下为无操作）
            SyncMenuState();
            RefreshMenuTheme(); // 打开前校准配色（跟随系统明暗）
        };

        foreach (var dd in SubMenus())
        {
            if (dd is ToolStripDropDownMenu subMenu)
            {
                subMenu.Renderer = MenuRenderer;
            }
            dd.HandleCreated += (_, _) => StyleMenuWindow(dd);
            dd.Opened += (_, _) => OnSubMenuOpened(dd);
        }
        Menu.HandleCreated += (_, _) => StyleMenuWindow(Menu);
        Menu.Opened += (_, _) =>
        {
            if (!MenuRenderer.CornersRounded(Menu))
            {
                ApplyRoundedRegion(Menu);
            }
        };

        Tray = new NotifyIcon
        {
            ContextMenuStrip = Menu,
            Visible = true,
            Text = "Numlock",
        };
        Tray.DoubleClick += (_, _) => ToggleByUser();
        s_current = this; // 未处理异常兜底需要一个活着的实例来弹提示（构造完成后再登记）

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        if (Config.EnforceNumLock)
        {
            EnsureHook(); // 钩子按需安装：未启用“保持常开”时，系统键盘完全不受本进程影响
            NumLock.Ensure(true); // 启动即校正；刷新在其后，图标/Tooltip 不会滞后一个看护周期
        }

        SyncMenuState();
        RefreshIcon(force: true);

        _nextAutoCheckMs = Environment.TickCount64 + 12_000; // 启动 12s 后首次自动检查（不抢启动路径）
        DetectStagedUpdate();                                // 上次退出时替换失败留下的 .new 也在此识别
        // 刚完成更新（自动替换/退出时替换/菜单“重启并更新”）时，新版首次启动提示一次“已更新到 X”
        _pendingUpdatedNotice = Updater.ConsumeUpdatedMarker(AppConfig.Dir, Updater.CurrentVersion) ?? "";

        _timer = new System.Windows.Forms.Timer { Interval = TimerIntervalMs };
        _timer.Tick += (_, _) => Watch();
        _timer.Start();

        if (showEvent != null)
        {
            _showEvent = showEvent;
            _showEventThread = new Thread(() =>
            {
                try
                {
                    while (!_stopShowThread && showEvent.WaitOne())
                    {
                        _showRunningBalloon = true;
                    }
                }
                catch (ObjectDisposedException)
                {
                    // 退出阶段句柄可能先于线程释放，忽略
                }
            })
            {
                IsBackground = true,
                Name = "Numlock.ShowEvent",
            };
            _showEventThread.Start();
        }
    }

    /// <summary>300ms 看护：保持常开时自动把关闭的 NumLock 拉回开启，并跟随状态重绘图标。</summary>
    private void Watch()
    {
        try
        {
            WatchCore();
        }
        catch
        {
            // 看护/重绘路径绝不因异常终止托盘进程（如 GDI+ 偶发失败）；下个周期照常继续
        }
    }

    private void WatchCore()
    {
        bool on = NumLock.IsOn;
        if (Config.EnforceNumLock)
        {
            EnsureHook(); // 按需补装：功能启用而钩子缺失时在这里恢复（安装失败则不再重试）
            RebuildHookIfDue(); // 60s 周期重挂：自愈被系统静默摘除的钩子
            if (!on)
            {
                // attempts:1 且短 settle：单次尝试，纠偏交给下一个 tick 继续，
                // 避免 SendInput 持续失败时每个 tick 长时间阻塞 UI 线程（钩子回调也无法分发）。
                // flipWaitMs 自适应：上一次失败（多半是注入被 UIPI 拦住）时把单次内部等待从 80ms 收到 25ms，
                // 每个 tick 的阻塞 ≈90ms → ≈35ms；重试节奏不变（仍每 300ms 先查状态再决定是否注入），
                // 恢复速度不受影响，也不会因等待缩短而重复注入（下一轮先读状态，已翻转就不再注入）。
                bool ok = NumLock.Ensure(true, attempts: 1, settleMs: 12, flipWaitMs: _enforceFailureNotified ? 25 : 80);
                if (ok)
                {
                    _enforceFailureNotified = false;
                }
                else if (!_enforceFailureNotified)
                {
                    _enforceFailureNotified = true;
                    ShowBalloon("NumLock 自动恢复失败", "可能被高权限窗口（UIPI）拦截，稍后会自动重试。", ToolTipIcon.Warning);
                }
                on = NumLock.IsOn;
            }
        }

        if (_showRunningBalloon)
        {
            _showRunningBalloon = false;
            ShowBalloon("Numlock 已在运行", "右键系统托盘中的键帽图标可打开菜单。", ToolTipIcon.Info);
            try { _ackEvent?.Set(); } catch { } // 通知第二实例“已确认”，让它安静退出
        }

        if (_configLoadStatus != ConfigLoadStatus.Ok)
        {
            ConfigLoadStatus status = _configLoadStatus;
            _configLoadStatus = ConfigLoadStatus.Ok;
            switch (status)
            {
                case ConfigLoadStatus.Corrupt:
                    ShowBalloon("配置文件已重置", "config.json 无法解析，已恢复默认设置（原文件已备份为 config.json.bad）。", ToolTipIcon.Warning);
                    break;
                case ConfigLoadStatus.CorruptNoBackup:
                    ShowBalloon("配置文件已重置", "config.json 无法解析，已恢复默认设置；原文件备份失败，仍留在原处。", ToolTipIcon.Warning);
                    break;
                default:
                    ShowBalloon("配置读取失败", "config.json 暂时无法读取（可能被其他程序占用），本次使用默认设置；之后修改设置会先尽力备份原文件。", ToolTipIcon.Warning);
                    break;
            }
        }

        PumpUpdateWork(); // 自动更新：到期检查 + 处理后台线程的结果（不在此阻塞）

        if (_displayRefresh)
        {
            _displayRefresh = false;
            RefreshIcon(force: true); // 分辨率/DPI 变化后重新取图标尺寸并重绘
        }

        if (_themeRefresh)
        {
            _themeRefresh = false;
            RefreshMenuTheme(); // 菜单配色随系统明暗更新
            // 偏好变更事件很频繁，只在“确实会影响图标”时才重绘：
            // ① 自动模式下明暗图标需要换色；② 系统小图标尺寸变化（切换“小任务栏按钮”等，图标须按新尺寸重绘）
            bool colorChanged = Config.IconColorMode == IconColor.Auto && ShouldUseWhiteIcons() != _lastWhite;
            bool sizeChanged = CurrentIconSize() != _lastIconSize;
            if (colorChanged || sizeChanged)
            {
                RefreshIcon(force: true);
                return;
            }
        }

        if (on != _lastOn || Config.EnforceNumLock != _lastEnforce)
        {
            RefreshIcon();
        }
    }

    private void ToggleByUser()
    {
        if (Config.EnforceNumLock)
        {
            NumLock.Ensure(true);
            RefreshIcon(force: true);
            return;
        }
        NumLock.Ensure(!NumLock.IsOn);
        RefreshIcon(force: true);
    }

    private void SetEnforce(bool on)
    {
        Config.EnforceNumLock = on;
        SaveConfig();
        if (on)
        {
            EnsureHook(); // 启用即挂钩子
            NumLock.Ensure(true);
        }
        else
        {
            UninstallHook(); // 关闭即卸钩子：此后系统键盘完全不受本进程影响
        }
        RefreshIcon(force: true);
        SyncMenuState();
    }

    private void ToggleAutoStart()
    {
        bool target = !AutoStart.IsEnabled();
        try
        {
            AutoStart.Set(target);
        }
        catch (Exception ex)
        {
            LogUnhandled(ex);
        }
        SyncMenuState();
    }

    private void SetIconColorMode(IconColor mode)
    {
        Config.IconColorMode = mode;
        SaveConfig();
        RefreshIcon(force: true);
        SyncMenuState();
    }

    // —————————————— 自动更新 ——————————————

    /// <summary>自检用：更新项。</summary>
    internal ToolStripMenuItem UpdateItemForTest => _updateItem;

    internal ToolStripMenuItem UpdateMenuItemForTest => _updateItem;

    internal ToolStripMenuItem CheckNowItemForTest => _updateItem;

    internal ToolStripMenuItem ApplyUpdateItemForTest => _updateItem;

    internal ToolStripMenuItem AutoCheckItemForTest => _autoCheckItem;

    /// <summary>自检用：当前识别到的“已就绪更新”版本号（空 = 无）。</summary>
    internal string StagedVersionTextForTest => StagedText;

    /// <summary>自检用：伪造“已就绪更新”，用于断言菜单项“重启并更新到 X”的动态显隐与文案。</summary>
    internal void SetStagedVersionForTest(Version? version)
    {
        _stagedVersion = version;
        SyncMenuState();
    }

    /// <summary>自检用：启动自动应用是否已排入（启动时发现已就绪更新即为 true）。</summary>
    internal bool AutoApplyPendingForTest => _autoApplyPending;

    /// <summary>自检用：模拟“启动时发现已就绪更新”，断言自动应用排程与菜单显隐（不触碰磁盘）。</summary>
    internal void SimulateStagedUpdateForTest(Version? version)
    {
        _stagedVersion = version;
        _autoApplyPending = version != null;
        SyncMenuState();
    }

    /// <summary>菜单项点击：已有就绪包则展示就绪更新卡片，否则打开检查中卡片并立即发起查询。</summary>
    private void OnUpdateMenuClicked()
    {
        if (_stagedVersion != null)
        {
            MacUpdateWindow.ShowReady(
                StagedText,
                () => ApplyStagedUpdateAndRestart(),
                Updater.PageUrl);
            return;
        }

        if (_downloadBusy)
        {
            MacUpdateWindow.ShowDownloading(() => CancelDownload());
            return;
        }

        MacUpdateWindow.ShowChecking(
            onRetry: () => RequestUpdateCheck(manual: true),
            pageUrl: Updater.PageUrl);

        if (!_updateBusy)
        {
            RequestUpdateCheck(manual: true);
            PumpUpdateWork();
        }
    }

    /// <summary>请求一次检查更新；manual=true（用户点菜单）时结果会主动在独立更新窗口告知。</summary>
    private void RequestUpdateCheck(bool manual)
    {
        _updateManualRequest = manual;
        _updateCheckRequested = true;
    }

    /// <summary>看护 tick 里的更新泵（UI 线程）：启动自动应用、到期启动后台检查、处理已完成的结果。</summary>
    private void PumpUpdateWork()
    {
        if (_pendingUpdatedNotice.Length > 0)
        {
            // 更新刚完成（一次性）：明确告知用户，避免“托盘图标闪了一下”被当成异常
            string done = _pendingUpdatedNotice;
            _pendingUpdatedNotice = "";
            MacUpdateWindow.ShowUpdateSuccess(done);
        }
        // 启动自动应用：发现已就绪更新时，本次启动直接换上新版并重启一次（用户无需操作）。
        // 覆盖“上次是关机/注销结束的，退出路径没机会替换”的情况。
        if (_autoApplyPending && !_updateBusy)
        {
            _autoApplyPending = false;
            if (TrySpawnApplyHelper(restart: true, quiet: true))
            {
                ExitApp(); // 辅助进程会等本进程退出后完成替换并重新启动（新版）
                return;
            }
            _pendingUpdateNotice = StagedText; // 拉不起辅助进程：退回“提示一次 + 菜单一键更新”
        }
        if (_updateDone)
        {
            _updateDone = false;
            HandleUpdateOutcome();
        }
        if (_updateBusy)
        {
            return;
        }
        if (_pendingUpdateNotice.Length > 0)
        {
            // 无法自动应用时提示一次（按版本去重），让用户知道重启更新即可生效
            string pending = _pendingUpdateNotice;
            _pendingUpdateNotice = "";
            Config.UpdateNotifiedVersion = pending;
            SaveConfig();
            MacUpdateWindow.ShowReady(pending, () => ApplyStagedUpdateAndRestart(), Updater.PageUrl);
        }
        bool due = Config.CheckUpdatesOnStart && Environment.TickCount64 >= _nextAutoCheckMs;
        if (!_updateCheckRequested && !(due && _stagedVersion == null))
        {
            return; // 已有就绪更新时不再自动重复检查（等用户重启更新即可）
        }
        if (due)
        {
            _nextAutoCheckMs = Environment.TickCount64 + 24 * 60 * 60 * 1000; // 自动检查每天最多一次
        }
        bool manual = _updateManualRequest;
        _updateManualRequest = false;
        _updateCheckRequested = false;
        bool alreadyStaged = _stagedVersion != null;
        _updateBusy = true;
        var t = new Thread(() => UpdateWorker(manual, alreadyStaged)) { IsBackground = true, Name = "Numlock.UpdateCheck" };
        t.Start();
    }

    /// <summary>后台线程：仅查询最新版本（解耦架构：不在此处下载更新包）。只写 volatile 字段与传参，不碰任何控件。</summary>
    private void UpdateWorker(bool manual, bool alreadyStaged)
    {
        int outcome = UpdateOutcomeFailed;
        string version = "";
        UpdateInfo? latestInfo = null;
        try
        {
            var (result, info) = Updater.Check();
            if (result == UpdateCheckResult.UpToDate)
            {
                outcome = UpdateOutcomeUpToDate;
                version = info?.Version.ToString(3) ?? "";
            }
            else if (result == UpdateCheckResult.Available && info != null)
            {
                version = info.Version.ToString(3);
                latestInfo = info;
                // 同一版本已暂存（比对 Version；Tag 是 "v1.1"、展示文案是 "1.1.0"，不能比字符串）则直接算就绪；
                // 否则仅标记 Available，由用户在更新卡片中确认后按需触发下载，绝不暗中阻塞下载 43MB。
                if (alreadyStaged && _stagedVersion == info.Version)
                {
                    outcome = UpdateOutcomeStaged;
                }
                else
                {
                    outcome = UpdateOutcomeAvailable;
                }
            }
        }
        catch
        {
            outcome = UpdateOutcomeFailed;
        }
        finally
        {
            _latestUpdateInfo = latestInfo;
            _updateVersionText = version;
            _updateManualResult = manual;
            _updateOutcome = outcome;
            _updateDone = true;
            _updateBusy = false;
        }
    }

    /// <summary>UI 线程处理一次更新检查的结果（已就绪/失败都按版本去重提示一次；无变化则保持安静）。</summary>
    private void HandleUpdateOutcome()
    {
        switch (_updateOutcome)
        {
            case UpdateOutcomeAvailable:
                if (_updateManualResult || MacUpdateWindow.IsVisible)
                {
                    var info = _latestUpdateInfo;
                    MacUpdateWindow.SetResultAvailable(
                        _updateVersionText,
                        () => StartDownloadAndStage(info),
                        info?.PageUrl ?? Updater.PageUrl);
                }
                else if (_updateVersionText.Length > 0 && !string.Equals(Config.UpdateNotifiedVersion, _updateVersionText, StringComparison.Ordinal))
                {
                    Config.UpdateNotifiedVersion = _updateVersionText;
                    SaveConfig();
                    var info = _latestUpdateInfo;
                    MacUpdateWindow.ShowAvailable(
                        _updateVersionText,
                        () => StartDownloadAndStage(info),
                        info?.PageUrl ?? Updater.PageUrl);
                }
                break;
            case UpdateOutcomeStaged:
                if (_updateVersionText.Length > 0 && !string.Equals(_updateVersionText, StagedText, StringComparison.Ordinal))
                {
                    _stagedVersion = Updater.ParseTag(_updateVersionText) ?? _stagedVersion;
                    if (!string.Equals(Config.UpdateNotifiedVersion, _updateVersionText, StringComparison.Ordinal))
                    {
                        Config.UpdateNotifiedVersion = _updateVersionText;
                        SaveConfig();
                    }
                }
                if (_updateManualResult || MacUpdateWindow.IsVisible)
                {
                    MacUpdateWindow.SetResultReady(_updateVersionText.Length > 0 ? _updateVersionText : StagedText, () => ApplyStagedUpdateAndRestart(), Updater.PageUrl);
                }
                break;
            case UpdateOutcomeStageFailed:
                if (_updateManualResult || MacUpdateWindow.IsVisible)
                {
                    var info = _latestUpdateInfo;
                    MacUpdateWindow.SetResultFailed(
                        $"下载新版本 {_updateVersionText} 失败。网络可能中断或安装目录不可写。",
                        () => StartDownloadAndStage(info),
                        info?.PageUrl ?? Updater.PageUrl);
                }
                break;
            case UpdateOutcomeUpToDate:
                if (_updateManualResult || MacUpdateWindow.IsVisible)
                {
                    MacUpdateWindow.SetResultUpToDate(Updater.CurrentVersion.ToString(3));
                }
                break;
            default:
                if (_updateManualResult || MacUpdateWindow.IsVisible)
                {
                    MacUpdateWindow.SetResultFailed(
                        "无法连接更新服务器，请检查网络后重试。",
                        () => RequestUpdateCheck(manual: true),
                        Updater.PageUrl);
                }
                break;
        }
        SyncMenuState();
    }

    /// <summary>用户确认后触发下载并校验暂存（解耦后的独立执行路径）。</summary>
    private void StartDownloadAndStage(UpdateInfo? info)
    {
        if (info == null)
        {
            RequestUpdateCheck(manual: true);
            return;
        }

        if (_downloadBusy)
        {
            return;
        }

        _downloadBusy = true;
        _downloadCts?.Cancel();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        MacUpdateWindow.ShowDownloading(() => CancelDownload());
        SyncMenuState();

        var progress = new Progress<(long DownloadedBytes, long TotalBytes)>(p =>
        {
            MacUpdateWindow.UpdateProgress(p.DownloadedBytes, p.TotalBytes);
        });

        var t = new Thread(() =>
        {
            bool ok = false;
            try
            {
                string exe = Environment.ProcessPath ?? "";
                ok = exe.Length > 0 && Updater.DownloadAndStage(info, exe, progress, ct);
            }
            catch
            {
                ok = false;
            }

            _downloadBusy = false;
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (Menu.IsHandleCreated)
            {
                Menu.BeginInvoke(new Action(() =>
                {
                    if (ok)
                    {
                        _stagedVersion = info.Version;
                        Config.UpdateNotifiedVersion = info.Version.ToString(3);
                        SaveConfig();
                        MacUpdateWindow.SetResultReady(
                            info.Version.ToString(3),
                            () => ApplyStagedUpdateAndRestart(),
                            info.PageUrl);
                    }
                    else
                    {
                        MacUpdateWindow.SetResultFailed(
                            $"下载新版本 v{info.Version.ToString(3)} 失败，请检查网络连接后重试。",
                            () => StartDownloadAndStage(info),
                            info.PageUrl);
                    }
                    SyncMenuState();
                }));
            }
        })
        {
            IsBackground = true,
            Name = "Numlock.UpdateDownload"
        };
        t.Start();
    }

    private void CancelDownload()
    {
        try
        {
            _downloadCts?.Cancel();
        }
        catch { }
        _downloadBusy = false;
        SyncMenuState();
    }

    private void ToggleAutoCheck()
    {
        Config.CheckUpdatesOnStart = !Config.CheckUpdatesOnStart;
        SaveConfig();
        if (Config.CheckUpdatesOnStart)
        {
            _nextAutoCheckMs = Environment.TickCount64 + 5_000; // 重新开启后很快查一次
        }
        SyncMenuState();
    }

    /// <summary>启动时识别同目录下已暂存的新版（上次退出时替换失败、关机/注销未走退出路径都会留在这里）。</summary>
    private void DetectStagedUpdate()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return;
            }
            Updater.TryCleanupStaged(exe); // 清掉“已经不新/读不出”的残留 .new、退休副本与过期临时文件
            var staged = Updater.FileVersionOf(Updater.StagedPath(exe));
            if (staged != null && Updater.IsNewer(staged, Updater.CurrentVersion))
            {
                _stagedVersion = staged;
                // 更自动：本次启动就直接换上并重启一次（覆盖“关机/注销没走到退出路径”的情况），用户无需操作；
                // 只有拉不起辅助进程时才退回“提示一次 + 菜单一键更新”。失败时辅助进程会把 .new 改名退休，
                // 因此不会出现“每次启动都重启一遍”的循环。
                _autoApplyPending = true;
            }
        }
        catch
        {
        }
    }

    /// <summary>菜单动作：用暂存的新版替换自己并重启。</summary>
    private void ApplyStagedUpdateAndRestart()
    {
        if (TrySpawnApplyHelper(restart: true, quiet: false))
        {
            ExitApp();
        }
    }

    /// <summary>拉起更新辅助进程（暂存的新版本自己）完成替换；quiet=true 时不弹任何提示（退出路径用）。
    /// 幂等：同一次更新只拉起一次（“重启并更新”随后会走 ExitApp→Shutdown，必须靠标志去重）。</summary>
    private bool TrySpawnApplyHelper(bool restart, bool quiet)
    {
        try
        {
            if (_applyHelperSpawned)
            {
                return true;
            }
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return false;
            }
            string staged = Updater.StagedPath(exe);
            // 版本门槛：只有确实比当前新才替换（防旧残留把好版本覆盖回去，也防同版本反复覆盖）
            var stagedVersion = File.Exists(staged) ? Updater.FileVersionOf(staged) : null;
            if (stagedVersion == null || !Updater.IsNewer(stagedVersion, Updater.CurrentVersion))
            {
                _stagedVersion = null;
                Updater.TryCleanupStaged(exe);
                SyncMenuState();
                if (!quiet)
                {
                    ShowBalloon("没有可用的更新", "未找到已下载的新版本，可先用菜单里的“检查更新”重新检查。", ToolTipIcon.Info);
                }
                return false;
            }
            bool ok = Updater.SpawnApplyHelper(staged, exe, Environment.ProcessId, restart);
            if (ok)
            {
                _applyHelperSpawned = true;
            }
            else if (!quiet)
            {
                ShowBalloon("更新失败", "无法启动更新进程（可能被安全软件拦截），已跳过本次更新。", ToolTipIcon.Warning);
            }
            return ok;
        }
        catch
        {
            return false;
        }
    }

    internal void SyncMenuState()
    {
        bool on = NumLock.IsOn;
        _brandHeader.UpdateStatus(Config.EnforceNumLock, on);
        _toggleItem.Text = on ? "关闭 NumLock" : "开启 NumLock";
        _toggleItem.Enabled = !Config.EnforceNumLock; // 保持常开时该动作不可用（灰显，与 macOS 习惯一致）
        _enforceItem.Checked = Config.EnforceNumLock;
        _autoStartItem.Checked = AutoStart.IsEnabled();
        _modeAuto.Checked = Config.IconColorMode == IconColor.Auto;
        _modeWhite.Checked = Config.IconColorMode == IconColor.White;
        _modeBlack.Checked = Config.IconColorMode == IconColor.Black;
        _autoCheckItem.Checked = Config.CheckUpdatesOnStart;

        if (_stagedVersion != null)
        {
            _updateItem.Text = "重启并更新到 " + StagedText;
            _updateItem.Enabled = true;
        }
        else if (_downloadBusy)
        {
            _updateItem.Text = "正在下载更新…";
            _updateItem.Enabled = false;
        }
        else if (_updateBusy)
        {
            _updateItem.Text = "正在检查更新…";
            _updateItem.Enabled = false;
        }
        else
        {
            _updateItem.Text = "检查更新…";
            _updateItem.Enabled = true;
        }
    }

    private void RefreshIcon(bool force = false)
    {
        try
        {
            RefreshIconCore(force);
        }
        catch
        {
            // 图标重绘失败不应终止进程；状态未更新，下个周期/下次操作会自然重试
        }
    }

    /// <summary>保存配置；若本次启动时配置因被占用而未能读取，先尽力把原文件留底为 .bad，避免用默认值静默覆盖用户设置。</summary>
    private bool SaveConfig()
    {
        if (_configBackupPending)
        {
            _configBackupPending = false;
            try { File.Copy(AppConfig.FilePath, AppConfig.FilePath + ".bad", overwrite: true); } catch { }
        }
        return Config.Save();
    }

    private void RefreshIconCore(bool force = false)
    {
        bool on = NumLock.IsOn;
        bool enforce = Config.EnforceNumLock;
        bool white = ShouldUseWhiteIcons();
        int size = CurrentIconSize();
        // 图标的所有输入（状态/保持常开/颜色/尺寸）都没变才跳过：漏掉尺寸会让系统换用不同大小的托盘图标后一直沿用旧位图
        if (!force && on == _lastOn && enforce == _lastEnforce && white == _lastWhite && size == _lastIconSize)
        {
            return;
        }

        using var bmp = IconFactory.RenderTrayIcon(size, on, white, enforce);
        var icon = IconFactory.ToIconAndDestroyHandle(bmp);
        var old = _currentIcon;
        Tray.Icon = icon;
        _currentIcon = icon;
        old?.Dispose();

        _lastOn = on;
        _lastEnforce = enforce;
        _lastWhite = white;
        _lastIconSize = size;
        Tray.Text = "NumLock：" + (on ? "已开启" : "已关闭") + (enforce ? "（保持常开）" : "");
    }

    /// <summary>
    /// 当前托盘小图标边长（像素）。按**任务栏窗口所在显示器**的 DPI 计算：
    /// 系统级 SM_CXSMICON 只反映主屏/系统 DPI，任务栏不在主屏且两屏缩放不同时会与任务栏实际需要的
    /// 尺寸不一致（图标被外壳缩放显示）；任务栏窗口探测或 API 不可用时回退系统度量。
    /// 异常小值兜底为 16。尺寸变化后须按新尺寸重绘。
    /// </summary>
    private static int CurrentIconSize()
    {
        int size = 0;
        try
        {
            IntPtr tray = Native.FindWindowW("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero)
            {
                uint dpi = Native.GetDpiForWindow(tray);
                if (dpi >= 96)
                {
                    size = Native.GetSystemMetricsForDpi(Native.SM_CXSMICON, dpi);
                }
            }
        }
        catch
        {
            size = 0; // 旧系统缺 GetDpiForWindow/GetSystemMetricsForDpi：回退系统度量
        }
        if (size <= 0)
        {
            size = SystemInformation.SmallIconSize.Width;
        }
        return size < 12 ? 16 : size;
    }

    /// <summary>自检用：托盘图标应渲染成的边长（与 CurrentIconSize 同源）。</summary>
    internal static int IconSizeForTest => CurrentIconSize();

    private bool ShouldUseWhiteIcons() => Config.IconColorMode switch
    {
        IconColor.White => true,
        IconColor.Black => false,
        _ => !SystemTheme.UsesLightTaskbar(),
    };

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        MacToast.Show(title, text, icon switch
        {
            ToolTipIcon.Warning => MacToast.ToastType.Warning,
            ToolTipIcon.Error => MacToast.ToastType.Error,
            _ => MacToast.ToastType.Info,
        });
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        _themeRefresh = true;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _displayRefresh = true;
    }

    /// <summary>菜单窗口外观：DWM 圆角（Win11；失败则回退为自绘区域裁剪）与深色模式帧。
    /// 子菜单例外：它左缘与父面板相邻，DWM 四角圆角必须关掉，圆角改由自绘 Region 只做右侧
    /// （否则接缝处两条描边并排、两个圆角各露一块背景缺口）。</summary>
    private void StyleMenuWindow(ToolStripDropDown dd)
    {
        try
        {
            bool isSubmenu = dd.OwnerItem != null;
            if (isSubmenu)
            {
                Native.TryDisableRoundCorners(dd.Handle);
            }
            bool rounded = !isSubmenu && Native.TryRoundCorners(dd.Handle);
            MenuRenderer.SetDwmCorners(dd, rounded);
            Native.SetImmersiveDarkMode(dd.Handle, MenuRenderer.Dark);
            Native.TrySetTransientBackdrop(dd.Handle);
            if (!rounded)
            {
                ApplyRoundedRegion(dd);
            }
        }
        catch
        {
        }
    }

    /// <summary>子菜单（“图标颜色”）：渲染器/窗口外观/度量/配色必须逐个应用，勿漏。</summary>
    private IEnumerable<ToolStripDropDown> SubMenus()
    {
        yield return _colorMenu.DropDown;
    }

    /// <summary>子菜单展开后：重绘父项（框架在打开瞬间把父项画成“不可用”样式且此后不再重绘），
    /// 并补做 DWM 圆角不可用时的自绘区域裁剪。</summary>
    private void OnSubMenuOpened(ToolStripDropDown dd)
    {
        if (dd.OwnerItem != null)
        {
            Menu.Invalidate(dd.OwnerItem.Bounds, false);
            Menu.Update();
        }
        if (!MenuRenderer.CornersRounded(dd))
        {
            ApplyRoundedRegion(dd);
        }
    }

    /// <summary>圆角区域裁剪兜底（不支持 DWM 圆角的系统）；子菜单只做右侧圆角，左缘保持直角。</summary>
    private static void ApplyRoundedRegion(ToolStripDropDown dd)
    {
        var m = MenuMetrics.For(dd.DeviceDpi, dwmCorners: false);
        RectangleF rect = new(0, 0, dd.Width, dd.Height);
        using var path = dd.OwnerItem != null
            ? Gfx.RightRoundedRect(rect, m.CornerRadius)
            : Gfx.RoundedRect(rect, m.CornerRadius);
        var old = dd.Region;
        dd.Region = new Region(path);
        old?.Dispose();
    }

    /// <summary>按当前显示器 DPI 应用菜单度量（构造、DPI 变化时）。</summary>
    private void ApplyMenuMetrics()
    {
        ApplyMenuMetrics(Menu);
        foreach (var dd in SubMenus())
        {
            ApplyMenuMetrics(dd);
        }
    }

    /// <summary>
    /// 度量随显示器 DPI 变化时重新应用（项度量在加入菜单前已按主显示器设好；
    /// 此处只处理跨显示器 DPI 变化：值变了才写，并让菜单按项重算尺寸）。
    /// </summary>
    private static void ApplyMenuMetrics(ToolStripDropDown dd)
    {
        try
        {
            var m = MenuMetrics.For(dd.DeviceDpi);
            float targetFontSize = 12f * m.Scale;
            if (dd.Font.Name != "Microsoft YaHei" || Math.Abs(dd.Font.Size - targetFontSize) > 0.1f || !dd.Font.Bold)
            {
                dd.Font = CreateMenuFont(m.Scale);
            }
            bool changed = false;
            for (int i = 0; i < dd.Items.Count; i++)
            {
                changed |= ApplyItemMetrics(dd.Items[i], i, dd.Items.Count, m);
            }
            if (changed)
            {
                dd.Size = dd.GetPreferredSize(Size.Empty);
            }
        }
        catch
        {
        }
    }

    private static Font CreateMenuFont(float scale)
    {
        try
        {
            return new Font("Microsoft YaHei", 12f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font(FontFamily.GenericSansSerif, 12f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        }
    }

    /// <summary>应用单个菜单项的 DPI 度量（构造期“加进菜单前”与 DPI 变化时共用同一套公式）；返回是否有变化。</summary>
    private static bool ApplyItemMetrics(ToolStripItem item, int index, int count, MenuMetrics m)
    {
        bool changed = false;
        var margin = new Padding(0, index == 0 ? m.MenuPad : 0, 0, index == count - 1 ? m.MenuPad : 0);
        if (!item.Margin.Equals(margin))
        {
            item.Margin = margin;
            changed = true;
        }
        if (item is ToolStripMenuItem mi)
        {
            var pad = new Padding(0, m.ItemPadV, 0, m.ItemPadV);
            if (!mi.Padding.Equals(pad))
            {
                mi.Padding = pad;
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>菜单配色跟随系统明暗（变化时才会真正刷新）。</summary>
    private void RefreshMenuTheme()
    {
        bool dark = !SystemTheme.UsesLightTaskbar();
        if (dark != MenuRenderer.Dark)
        {
            SetMenuTheme(dark);
        }
    }

    /// <summary>设置菜单配色并同步窗口深色帧（产线主题切换与自检强制配色共用）。</summary>
    internal void SetMenuTheme(bool dark)
    {
        MenuRenderer.Dark = dark;
        if (Menu.IsHandleCreated)
        {
            Native.SetImmersiveDarkMode(Menu.Handle, dark);
        }
        foreach (var dd in SubMenus())
        {
            if (dd.IsHandleCreated)
            {
                Native.SetImmersiveDarkMode(dd.Handle, dark);
            }
            dd.Invalidate();
        }
        Menu.Invalidate();
    }

    private void EnsureHook() => _hookGuard.EnsureHook();

    private void UninstallHook() => _hookGuard.UninstallHook();

    private void RebuildHookIfDue() => _hookGuard.RebuildHookIfDue();

    /// <summary>吞键判定（抽成纯函数便于自检断言）：物理 NumLock 的按下/抬起一律拦截，注入事件放行。</summary>
    internal static bool IsSwallowTarget(int msg, uint vkCode, uint flags) =>
        KeyboardHookGuard.IsSwallowTarget(msg, vkCode, flags);

    private static void ShowAbout()
    {
        string? infoVer = typeof(TrayApp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrEmpty(infoVer))
        {
            int plus = infoVer.IndexOf('+');
            if (plus > 0) infoVer = infoVer[..plus];
        }
        string version = !string.IsNullOrEmpty(infoVer)
            ? infoVer
            : (typeof(TrayApp).Assembly.GetName().Version?.ToString(3) ?? "2.0.3");
        MacAboutWindow.ShowAbout(version);
    }

    /// <summary>登记当前实例：未处理异常兜底需要一个活着的实例来弹提示。</summary>
    private static TrayApp? s_current;

    private static bool s_uiErrorNotified;

    private static int s_uiExceptionCount;

    /// <summary>自检用：本进程内 UI 线程未处理异常的累计次数（自检断言其必须为 0）。</summary>
    internal static int UiExceptionCount => s_uiExceptionCount;

    /// <summary>
    /// UI 线程未处理异常兜底（由 Program 注册到 Application.ThreadException）：
    /// 记录日志并保持托盘存活，避免一次异常（自绘/菜单回调/GDI+ 偶发失败）就整体退出。
    /// 本方法自身绝不抛异常。
    /// </summary>
    internal static void ReportUnhandledUiException(Exception? ex)
    {
        s_uiExceptionCount++;
        LogUnhandled(ex);
        try
        {
            if (!s_uiErrorNotified && s_current != null)
            {
                s_uiErrorNotified = true; // 只提示一次，避免异常反复时刷气泡
                s_current.ShowBalloon("Numlock 内部错误", "程序已继续运行（详情见 %AppData%\\Numlock\\error.log）。", ToolTipIcon.Warning);
            }
        }
        catch
        {
            // 兜底路径自身不再抛
        }
    }

    /// <summary>把未处理异常追加到 %AppData%\Numlock\error.log（尽力而为；超出 256KB 时整体重写，避免无界增长）。</summary>
    internal static void LogUnhandled(Exception? ex)
    {
        if (ex == null)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(AppConfig.Dir);
            string path = Path.Combine(AppConfig.Dir, "error.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
            {
                File.Delete(path);
            }
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不影响运行
        }
    }

    internal void Shutdown()
    {
        if (_shuttingDown)
        {
            return;
        }
        _shuttingDown = true;
        s_current = null;
        TrySpawnApplyHelper(restart: false, quiet: true); // 有暂存更新就静默替换（下次启动即新版本）

        _stopShowThread = true;
        _showEvent?.Set();           // 唤醒等待线程，令其观察到退出标志
        _showEventThread?.Join(200); // 等线程退出后再随 Main 释放句柄

        CancelDownload();
        _timer?.Stop();
        _timer?.Dispose();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        UninstallHook();

        Tray.Icon = null;
        Tray.Visible = false;
        Tray.Dispose();
        // 菜单不在退出路径内显式 Dispose：避免在菜单项点击回调中释放自身，交进程退出回收
        var icon = _currentIcon;
        _currentIcon = null;
        icon?.Dispose();
    }

    private void ExitApp()
    {
        Shutdown();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Shutdown();
            _hookGuard.Dispose();
        }
        base.Dispose(disposing);
    }
}
