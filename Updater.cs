using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Numlock;

/// <summary>线上最新版本的完整信息（下载所需的一切）。</summary>
internal sealed class UpdateInfo
{
    /// <summary>归一化后的四段版本号。</summary>
    public Version Version { get; init; } = new(0, 0, 0, 0);

    public string Tag { get; init; } = "";

    /// <summary>Numlock.zip 的下载地址。</summary>
    public string AssetUrl { get; init; } = "";

    /// <summary>期望的 zip SHA-256（十六进制小写）；线上没提供时为 null。</summary>
    public string? Sha256 { get; init; }

    /// <summary>更新包文件大小（字节）；线上未提供或未解析出时为 0。</summary>
    public long AssetSizeBytes { get; init; }

    /// <summary>Release 页面地址（供“打不开自动更新”时手动下载）。</summary>
    public string PageUrl { get; init; } = "";
}

internal enum UpdateCheckResult
{
    UpToDate,
    Available,
    Failed, // 网络不可达 / 响应异常 / 解析失败
}

/// <summary>
/// 自动更新：查询 GitHub 上的最新 Release → 校验并下载 Numlock.zip → 取出新的 Numlock.exe 暂存为
/// <c>Numlock.exe.new</c> → 由 <c>--apply-update</c> 辅助模式在旧进程退出后替换并（可选）重启。
///
/// 设计约束（勿回退）：
/// · 只有“检查更新/下载”会联网，其余功能完全离线；检查失败一律静默（自动路径）或一次性气泡（手动路径）。
/// · 应用更新不打断用户：默认在退出时替换文件，下次启动即新版本；用户也可在菜单里选“重启并更新”。
/// · 下载必须校验 SHA-256（GitHub 资产 digest 或同级 .sha256 资产），并拒绝含路径穿越条目的压缩包。
/// · 替换失败（安装目录只读/被占用）时，若用户选的是“重启并更新”，仍要把旧版本重新拉起，不留“托盘图标消失”。
/// </summary>
internal static class Updater
{
    internal const string Owner = "Crucialmer";
    internal const string Repo = "Numlock";
    internal const string AssetName = "Numlock.zip";
    internal const string ExeName = "Numlock.exe";
    internal const string StagedSuffix = ".new";
    internal const string ApplySwitch = "--apply-update";
    internal const string CheckSwitch = "--check-update";

    private const string ApiLatest = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";
    internal const string PageUrl = "https://github.com/" + Owner + "/" + Repo + "/releases";
    private const int RequestTimeoutMs = 8_000;
    private const int DownloadTimeoutMs = 10 * 60_000;
    private const long MaxDownloadBytes = 200L * 1024 * 1024;  // 更新包体积上限（当前小包约 180 KB）
    private const long MaxStagedBytes = 300L * 1024 * 1024;    // 解出的可执行文件体积上限

    internal static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0);

    internal static string StagedPath(string targetExe) => targetExe + StagedSuffix;

    /// <summary>把任意段数的版本号补成四段，便于比较（"1.1" → 1.1.0.0）。</summary>
    internal static Version Normalize(Version v) =>
        new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));

    /// <summary>解析 Release 标签（v1.1 / 1.1 / V1.1.2 / 1.1.0-beta 都接受前缀数字部分）；失败返回 null。</summary>
    internal static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }
        string s = tag.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
        {
            s = s[1..];
        }
        // 去掉预发布后缀（1.1.0-beta / 1.1+build），只比较数字部分
        int cut = s.IndexOfAny(['-', '+']);
        if (cut > 0)
        {
            s = s[..cut];
        }
        if (!Version.TryParse(s, out var v))
        {
            return null;
        }
        var n = Normalize(v);
        return n.Major <= 0 && n.Minor <= 0 && n.Build <= 0 ? null : n;
    }

    internal static bool IsNewer(Version candidate, Version current) =>
        Normalize(candidate) > Normalize(current);

    /// <summary>解析 GitHub releases/latest 的 JSON；内容畸形、缺资产或缺标签时返回 null（不抛异常）。</summary>
    internal static UpdateInfo? ParseLatestRelease(string json)
    {
        try
        {
            return ParseLatestReleaseCore(json);
        }
        catch
        {
            return null; // 限流页/HTML 错误页/截断响应等一律按“检查失败”处理
        }
    }

    private static UpdateInfo? ParseLatestReleaseCore(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var version = ParseTag(tag);
        if (version == null)
        {
            return null;
        }
        string page = root.TryGetProperty("html_url", out var p) ? p.GetString() ?? PageUrl : PageUrl;

        string? assetUrl = null;
        string? digest = null;
        long assetSize = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                string? name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (!string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                assetUrl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out long sVal))
                {
                    assetSize = sVal;
                }
                // GitHub 自 2024 起在资产上提供 digest: "sha256:<hex>"；老响应没有该字段
                string? d = a.TryGetProperty("digest", out var dg) ? dg.GetString() : null;
                if (!string.IsNullOrEmpty(d) && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    digest = d["sha256:".Length..].Trim().ToLowerInvariant();
                }
            }
        }
        if (string.IsNullOrEmpty(assetUrl))
        {
            return null;
        }
        return new UpdateInfo
        {
            Version = version,
            Tag = tag ?? version.ToString(),
            AssetUrl = assetUrl,
            Sha256 = digest,
            PageUrl = page,
            AssetSizeBytes = assetSize,
        };
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(3), // 限制 TCP 握手 3s，避免在丢包/受限节点死等系统默认 21s
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        var c = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(RequestTimeoutMs),
        };
        // GitHub API 要求带 User-Agent（缺失会 403）
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Numlock/" + CurrentVersion.ToString(3));
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    private static readonly HttpClient Http = CreateClient();

    /// <summary>查询线上最新版本（阻塞；必须由后台线程调用）。具备官方 API + Web 重定向双通道极速备用探针。</summary>
    internal static (UpdateCheckResult Result, UpdateInfo? Info) Check()
    {
        // 1. 优先通过 GitHub Releases API 检索完整元数据与 SHA-256 签名
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ApiLatest);
            using var resp = Http.Send(req, HttpCompletionOption.ResponseContentRead);
            if (resp.IsSuccessStatusCode)
            {
                using var reader = new StreamReader(resp.Content.ReadAsStream());
                string json = reader.ReadToEnd();
                var info = ParseLatestRelease(json);
                if (info != null)
                {
                    return IsNewer(info.Version, CurrentVersion)
                        ? (UpdateCheckResult.Available, info)
                        : (UpdateCheckResult.UpToDate, info);
                }
            }
        }
        catch
        {
            // API 超时、限流或受阻：不直接放弃，无缝降级到 Web 重定向探针
        }

        // 2. 备用通道：向 github.com/releases/latest 请求（走 Fastly CDN，仅读响应头，极速重定向到 /tag/vX.X.X）
        try
        {
            string latestWebUrl = "https://github.com/" + Owner + "/" + Repo + "/releases/latest";
            using var req = new HttpRequestMessage(HttpMethod.Get, latestWebUrl);
            using var resp = Http.Send(req, HttpCompletionOption.ResponseHeadersRead);
            var finalUri = resp.RequestMessage?.RequestUri;
            if (finalUri != null && finalUri.AbsolutePath.Contains("/releases/tag/"))
            {
                string tag = finalUri.Segments.LastOrDefault()?.Trim('/') ?? "";
                var v = ParseTag(tag);
                if (v != null)
                {
                    string assetUrl = $"https://github.com/{Owner}/{Repo}/releases/download/{tag}/{AssetName}";
                    var info = new UpdateInfo
                    {
                        Version = v,
                        Tag = tag,
                        AssetUrl = assetUrl,
                        Sha256 = null, // 稍后下载时由 TryFetchSiblingHash 检索 .sha256 校验文件
                        PageUrl = finalUri.ToString(),
                    };
                    return IsNewer(info.Version, CurrentVersion)
                        ? (UpdateCheckResult.Available, info)
                        : (UpdateCheckResult.UpToDate, info);
                }
            }
        }
        catch
        {
            // 离线/全链路不可达：静默失败
        }

        return (UpdateCheckResult.Failed, null);
    }

    /// <summary>下载 zip、校验 SHA-256、从压缩包中取出 Numlock.exe 暂存为 xxx.exe.new。返回 true 表示已就绪。
    /// 校验是硬门槛（fail-closed）：拿不到期望哈希就拒绝暂存，绝不"无校验更新"。</summary>
    internal static bool DownloadAndStage(
        UpdateInfo info,
        string targetExe,
        IProgress<(long DownloadedBytes, long TotalBytes)>? progress = null,
        CancellationToken cancelToken = default)
    {
        string? tmpZip = null;
        try
        {
            tmpZip = Path.Combine(Path.GetTempPath(), "numlock-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
            using var timeoutCts = new CancellationTokenSource(DownloadTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancelToken);
            var token = linkedCts.Token;

            using (var resp = Http.Send(new HttpRequestMessage(HttpMethod.Get, info.AssetUrl),
                       HttpCompletionOption.ResponseHeadersRead, token))
            {
                if (!resp.IsSuccessStatusCode)
                {
                    return false;
                }
                long totalBytes = resp.Content.Headers.ContentLength ?? 0L;
                using var src = resp.Content.ReadAsStream(token);
                using var dst = File.Create(tmpZip);
                byte[] buffer = new byte[81920];
                long total = 0;
                int read;
                long lastReportTick = 0;
                while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    total += read;
                    if (total > MaxDownloadBytes) // 体积上限：防超长响应/无限流写满磁盘
                    {
                        Log("更新包超过体积上限，已中止下载");
                        return false;
                    }
                    dst.Write(buffer, 0, read);

                    long now = Environment.TickCount64;
                    if (now - lastReportTick >= 50)
                    {
                        lastReportTick = now;
                        progress?.Report((total, totalBytes));
                    }
                }
                progress?.Report((total, totalBytes));
            }
            string? expected = info.Sha256;
            if (string.IsNullOrEmpty(expected))
            {
                // 线上没给 digest：退回读取同名的 .sha256 资产
                expected = TryFetchSiblingHash(info.AssetUrl, token);
            }
            if (string.IsNullOrEmpty(expected))
            {
                Log("线上未提供 SHA-256（digest 与 .sha256 资产均不可用），拒绝暂存未校验的更新");
                return false;
            }
            token.ThrowIfCancellationRequested();
            if (!VerifySha256(tmpZip, expected))
            {
                Log("更新包 SHA-256 校验失败，已丢弃（期望 " + expected + "）");
                return false;
            }
            token.ThrowIfCancellationRequested();
            string staged = StagedPath(targetExe);
            if (!TryExtractExe(tmpZip, staged))
            {
                return false;
            }
            var stagedVersion = FileVersionOf(staged);
            if (stagedVersion == null || !IsNewer(stagedVersion, CurrentVersion))
            {
                // 下载到的可执行文件版本不比当前新（例如线上资产与 tag 不一致）：不更新，避免“更新后还是旧版”
                Log("更新包内版本号不高于当前版本，已丢弃");
                TryDelete(staged);
                return false;
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            Log("更新包下载已取消");
            return false;
        }
        catch (Exception ex)
        {
            Log("下载/暂存更新失败: " + ex.Message);
            return false;
        }
        finally
        {
            if (tmpZip != null)
            {
                TryDelete(tmpZip);
            }
        }
    }

    private static string? TryFetchSiblingHash(string assetUrl, CancellationToken token = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, assetUrl + ".sha256");
            using var resp = Http.Send(req, HttpCompletionOption.ResponseContentRead, token);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }
            using var reader = new StreamReader(resp.Content.ReadAsStream(token));
            string text = reader.ReadToEnd().Trim();
            string hex = text.Split(' ', '\t', '*', '\r', '\n')[0].Trim().ToLowerInvariant();
            return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex : null;
        }
        catch
        {
            return null;
        }
    }

    internal static bool VerifySha256(string file, string expectedHex)
    {
        try
        {
            using var fs = File.OpenRead(file);
            using var sha = SHA256.Create();
            string actual = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
            return string.Equals(actual, expectedHex.Trim().ToLowerInvariant(), StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从 zip 中取出 <c>Numlock/Numlock.exe</c>：优先精确匹配该路径，其次任意层级的同名文件；
    /// 出现多个候选或条目含路径穿越/绝对路径/盘符时整体拒绝。校验 PE（MZ + PE\0\0 签名）与体积上限后才落盘。
    /// </summary>
    internal static bool TryExtractExe(string zipPath, string destPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            ZipArchiveEntry? exact = null;
            ZipArchiveEntry? loose = null;
            foreach (var e in zip.Entries)
            {
                string full = e.FullName.Replace('\\', '/');
                // 逐段判断穿越（"使用说明..txt" 这类合法名字不该被误杀）
                foreach (string seg in full.Split('/'))
                {
                    if (seg == ".." || seg.Contains(':'))
                    {
                        return false;
                    }
                }
                if (full.StartsWith('/'))
                {
                    return false;
                }
                if (string.Equals(Path.GetFileName(full), ExeName, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(full, "Numlock/" + ExeName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (exact != null)
                        {
                            return false; // 重复条目：整体拒绝
                        }
                        exact = e;
                    }
                    else
                    {
                        if (loose != null || exact != null)
                        {
                            return false; // 名称重复（含“一个标准路径 + 一个别名路径”）：可疑包，整体拒绝
                        }
                        loose = e;
                    }
                }
            }
            ZipArchiveEntry? hit = exact ?? loose;
            if (hit == null || hit.Length <= 0 || hit.Length > MaxStagedBytes)
            {
                return false;
            }
            string tmp = destPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp"; // 随机名：避免跨进程/跨会话互相覆盖
            using (var src = hit.Open())
            using (var dst = File.Create(tmp))
            {
                src.CopyTo(dst);
            }
            if (!LooksLikePe(tmp))
            {
                TryDelete(tmp);
                return false;
            }
            File.Move(tmp, destPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Log("解压更新包失败: " + ex.Message);
            TryDelete(destPath + ".tmp");
            return false;
        }
    }

    /// <summary>PE 校验：MZ 头 + e_lfanew 指向的 "PE\0\0" 签名（只验两个字节不足以排除 HTML/随机数据）。</summary>
    private static bool LooksLikePe(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (fs.Length < 0x40)
            {
                return false;
            }
            Span<byte> head = stackalloc byte[0x40];
            if (fs.Read(head) != head.Length || head[0] != (byte)'M' || head[1] != (byte)'Z')
            {
                return false;
            }
            int lfanew = BitConverter.ToInt32(head[0x3C..0x40]);
            if (lfanew < 0x40 || lfanew > fs.Length - 4)
            {
                return false;
            }
            fs.Position = lfanew;
            Span<byte> sig = stackalloc byte[4];
            return fs.Read(sig) == 4 && sig[0] == (byte)'P' && sig[1] == (byte)'E' && sig[2] == 0 && sig[3] == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>读取可执行文件的 FileVersion；失败返回 null。</summary>
    internal static Version? FileVersionOf(string path)
    {
        try
        {
            string? v = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion;
            return Version.TryParse(v, out var parsed) ? Normalize(parsed) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 替换核心：等旧进程退出（最多 timeoutMs）后，把 selfPath（暂存的新版）复制覆盖 targetExe。
    /// 用复制而非移动——辅助进程正运行在 selfPath 上（运行中的可执行文件可读不可写/删）；
    /// 覆盖采用重试，兼容杀毒软件短暂占用。返回错误描述（null = 成功）。
    /// </summary>
    internal static string? ApplyCore(string selfPath, string targetExe, int waitForPid, int timeoutMs)
    {
        if (waitForPid > 0)
        {
            long deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (!IsProcessAlive(waitForPid))
                {
                    break;
                }
                Thread.Sleep(200);
            }
        }
        long copyDeadline = Environment.TickCount64 + 30_000;
        string? lastError = null;
        while (Environment.TickCount64 < copyDeadline)
        {
            try
            {
                File.Copy(selfPath, targetExe, overwrite: true);
                return null;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                Thread.Sleep(400);
            }
        }
        return lastError ?? "未知错误";
    }

    internal static bool IsProcessAlive(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>拉起辅助进程（暂存的新版本自己）去完成替换；restart=true 时替换成功后重启程序。</summary>
    internal static bool SpawnApplyHelper(string selfPath, string targetExe, int pid, bool restart)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(selfPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(targetExe) ?? ".",
            };
            psi.ArgumentList.Add(ApplySwitch);
            psi.ArgumentList.Add(targetExe);
            psi.ArgumentList.Add(pid.ToString(CultureInfo.InvariantCulture));
            if (restart)
            {
                psi.ArgumentList.Add("--restart");
            }
            return System.Diagnostics.Process.Start(psi) != null;
        }
        catch (Exception ex)
        {
            Log("无法启动更新辅助进程: " + ex.Message);
            return false;
        }
    }

    /// <summary>辅助模式实现（--apply-update &lt;目标exe&gt; &lt;旧PID&gt; [--restart]）。退出码 0=成功。</summary>
    internal static int RunApplyHelper(string[] args)
    {
        if (args.Length < 2)
        {
            return 2;
        }
        string target = Path.GetFullPath(args[0]);
        int pid = int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) ? p : 0;
        bool restart = args.Any(a => a.Equals("--restart", StringComparison.OrdinalIgnoreCase));
        string self = Environment.ProcessPath ?? "";
        // 只接受“自己就是暂存的新版副本”这一种形态：避免误把任意 exe 当更新包复制到目标上
        if (self.Length == 0 || !self.EndsWith(StagedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            Log("更新辅助进程被以非暂存副本的身份调用，已忽略");
            return 2;
        }
        var selfVersion = FileVersionOf(self);
        var targetVersion = FileVersionOf(target);
        if (selfVersion == null || (targetVersion != null && !IsNewer(selfVersion, targetVersion)))
        {
            // 暂存副本不比目标新（重复拉起 / 目标已被别的进程更新过）：不替换，并让自己“退休”免得下次退出再来一遍
            Log($"跳过替换：暂存版本 {selfVersion?.ToString(3) ?? "?"} 不高于目标版本 {targetVersion?.ToString(3) ?? "?"}");
            RetireStaged(self);
            if (restart)
            {
                StartTarget(target);
            }
            return 0;
        }
        string? err = ApplyCore(self, target, pid, 60_000);
        if (err != null)
        {
            Log("更新替换失败: " + err);
            RetireStaged(self); // 运行中的镜像删不掉：改名退休，由下次启动清理
        }
        else
        {
            // 替换成功：留一个标记，让新版首次启动时提示一次“已更新到 X”
            MarkUpdated(selfVersion.ToString(3), AppConfig.Dir);
        }
        if (restart)
        {
            // 无论替换成功与否都把程序拉起来：成功=新版，失败=旧版（不让用户面对“托盘图标没了”）
            StartTarget(target);
        }
        return err == null ? 0 : 1;
    }

    /// <summary>把暂存副本改名“退休”：运行中的可执行文件不能删也不能覆盖，但允许改名。
    /// 保留到下次启动由 TryCleanupStaged 清理——避免每次退出都再拉起一个注定失败的辅助进程。</summary>
    private static void RetireStaged(string self)
    {
        try
        {
            string retired = self + ".done";
            TryDelete(retired);
            File.Move(self, retired, overwrite: true);
        }
        catch (Exception ex)
        {
            Log("退休暂存副本失败: " + ex.Message);
        }
    }

    private static void StartTarget(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("更新后重启失败: " + ex.Message);
        }
    }

    /// <summary>“已更新到 X”一次性提示的标记文件（放在配置目录，不污染程序所在文件夹）。</summary>
    internal static string UpdatedMarkerPath(string dir) => Path.Combine(dir, "updated.txt");

    /// <summary>替换成功后由辅助进程写下标记：新版首次启动时据此提示一次“已更新到 X”。</summary>
    internal static void MarkUpdated(string version, string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(UpdatedMarkerPath(dir), version);
        }
        catch (Exception ex)
        {
            Log("写入更新完成标记失败: " + ex.Message);
        }
    }

    /// <summary>新版首次启动时读取并消费“已更新”标记：只有标记版本与当前运行版本一致才返回版本号
    /// （表示确实刚更新到本版）；无论是否匹配都会删除标记，确保提示只出现一次。</summary>
    internal static string? ConsumeUpdatedMarker(string dir, Version current)
    {
        string path = UpdatedMarkerPath(dir);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            string text = File.ReadAllText(path).Trim();
            File.Delete(path);
            var v = ParseTag(text);
            return v != null && Normalize(v) == Normalize(current) ? Normalize(v).ToString(3) : null;
        }
        catch
        {
            TryDelete(path);
            return null;
        }
    }

    /// <summary>启动时清理更新残留：已不新/读不出版本的 .new、退休的 .done、遗留的临时解压文件与过期临时包。</summary>
    internal static void TryCleanupStaged(string targetExe)
    {
        string staged = StagedPath(targetExe);
        if (File.Exists(staged))
        {
            var sv = FileVersionOf(staged);
            if (sv == null || !IsNewer(sv, CurrentVersion))
            {
                TryDelete(staged); // 已经不新或读不出：清掉，避免退出时把好版本覆盖回旧版
            }
        }
        TryDelete(staged + ".done");
        try
        {
            string dir = Path.GetDirectoryName(targetExe) ?? ".";
            foreach (string f in Directory.GetFiles(dir, Path.GetFileName(staged) + ".*.tmp"))
            {
                TryDelete(f);
            }
        }
        catch
        {
        }
        try
        {
            foreach (string f in Directory.GetFiles(Path.GetTempPath(), "numlock-*.zip"))
            {
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-1))
                {
                    TryDelete(f); // 半途退出的下载残留；只清一天前的，避免误删正在进行的下载
                }
            }
        }
        catch
        {
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 被占用/无权限：留待下次
        }
    }

    private static void Log(string message) =>
        TrayApp.LogUnhandled(new InvalidOperationException("[updater] " + message));
}
