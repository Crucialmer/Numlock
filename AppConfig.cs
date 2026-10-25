using System.Text.Json;
using System.Text.Json.Serialization;

namespace Numlock;

/// <summary>图标颜色模式（JSON 中存为 auto / white / black）。</summary>
internal enum IconColor
{
    Auto,
    White,
    Black,
}

/// <summary>配置读取结果。</summary>
internal enum ConfigLoadStatus
{
    Ok,         // 正常读取（或文件不存在 = 全新安装）
    Corrupt,    // 内容损坏：已备份为 config.json.bad 并回退默认值
    CorruptNoBackup, // 内容损坏且备份失败：原文件留在原处并回退默认值
    ReadError,  // 暂时无法读取（被占用/无权限）：原文件未动，本次使用默认值
}

/// <summary>IconColor 的 JSON 转换器：读写 auto/white/black，大小写不敏感；其余值（含非字符串）一律归一到 Auto。</summary>
internal sealed class IconColorConverter : JsonConverter<IconColor>
{
    public override IconColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString()?.Trim().ToLowerInvariant() switch
            {
                "white" => IconColor.White,
                "black" => IconColor.Black,
                _ => IconColor.Auto,
            };
        }
        reader.Skip(); // 非字符串（数字/布尔/null/对象/数组）按 Auto 处理，并消费掉对象/数组子树
        return IconColor.Auto;
    }

    public override void Write(Utf8JsonWriter writer, IconColor value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString().ToLowerInvariant());
}

/// <summary>应用配置（config.json）</summary>
internal sealed class AppConfig
{
    /// <summary>保持常开：自动把 NumLock 恢复为开启，并拦截键盘上的 NumLock 键。</summary>
    public bool EnforceNumLock { get; set; }

    /// <summary>图标颜色：Auto（跟随系统）/ White（白色，深色任务栏）/ Black（黑色，浅色任务栏）。</summary>
    public IconColor IconColorMode { get; set; } = IconColor.Auto;

    /// <summary>启动后自动检查更新（默认开启）。关闭后仅菜单里的“立即检查更新”会联网。</summary>
    public bool CheckUpdatesOnStart { get; set; } = true;

    /// <summary>最近一次已向用户提示过的更新版本（同一版本只提示一次）。空 = 从未提示。</summary>
    public string? UpdateNotifiedVersion { get; set; }

    /// <summary>数据目录：严格位于程序所在目录（纯绿色免安装，产生的文件绝不出现在本文件夹以外）。</summary>
    public static string Dir { get; } = AppDomain.CurrentDomain.BaseDirectory;

    public static string FilePath { get; } = Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true, // 手改配置时属性名大小写不敏感
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // 未设置的项不落盘，配置文件保持干净
        Converters = { new IconColorConverter() },
    };

    /// <summary>解析配置文本（供自检直接断言解析规则；解析失败会抛出，由调用方兜底）。</summary>
    internal static AppConfig Parse(string json) =>
        JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();

    /// <summary>读取配置并报告结果（损坏时备份为 config.json.bad；暂时读不到时不动原文件）。</summary>
    public static AppConfig Load(out ConfigLoadStatus status) => LoadFrom(FilePath, out status);

    /// <summary>从指定路径读取配置（自检用临时文件直接覆盖各分支）。</summary>
    internal static AppConfig LoadFrom(string path, out ConfigLoadStatus status)
    {
        status = ConfigLoadStatus.Ok;
        try
        {
            if (File.Exists(path))
            {
                return Parse(File.ReadAllText(path));
            }
        }
        catch (JsonException)
        {
            // 内容损坏：备份原文件后回退默认值（备份失败则如实标记，不假称已备份）
            status = ConfigLoadStatus.Corrupt;
            try
            {
                File.Move(path, path + ".bad", overwrite: true);
            }
            catch
            {
                status = ConfigLoadStatus.CorruptNoBackup;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 暂时读不到（被编辑器/安全软件占用等）：回退默认值但原文件保持原样
            status = ConfigLoadStatus.ReadError;
        }
        return new AppConfig();
    }

    /// <summary>保存配置（先写临时文件再原子替换），返回是否成功。</summary>
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
            File.Move(tmp, FilePath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

