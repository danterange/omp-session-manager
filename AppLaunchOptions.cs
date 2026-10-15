namespace OmpSessionManager;

/// <summary>
/// 表示仅用于测试、演示或隔离运行的启动选项；未提供选项时始终沿用用户的默认 OMP 数据位置。
/// </summary>
public sealed class AppLaunchOptions
{
    private const string AgentDirectoryOption = "--agent-directory";
    private const string IndexFileOption = "--index-file";
    private const string OmpExecutableOption = "--omp-executable";
    private const string DisableWindowScanOption = "--disable-window-scan";
    private const string DisableAutoRefreshOption = "--disable-auto-refresh";
    private const string RedactFilePathOption = "--redact-file-path";

    /// <summary>获取可选的 OMP agent 数据根目录；为空时扫描默认用户目录。</summary>
    public string? AgentDirectory { get; }

    /// <summary>获取可选的管理器索引文件；为空时使用默认 LocalAppData 位置。</summary>
    public string? IndexFilePath { get; }

    /// <summary>获取可选的 OMP 可执行文件路径；为空时按默认位置和 PATH 查找。</summary>
    public string? OmpExecutablePath { get; }

    /// <summary>获取是否跳过当前桌面窗口扫描，供隔离 UI 测试避免读取真实窗口信息。</summary>
    public bool DisableWindowScan { get; }

    /// <summary>获取是否停用 15 秒定时刷新，供演示和 UI 测试保持稳定画面。</summary>
    public bool DisableAutoRefresh { get; }

    /// <summary>获取是否在详情区隐藏原始 JSONL 的本机绝对路径，供安全演示和截图使用。</summary>
    public bool RedactFilePath { get; }

    /// <summary>
    /// 创建启动选项，并将空白路径归一为默认行为，避免测试配置意外覆盖用户数据。
    /// </summary>
    public AppLaunchOptions(
        string? agentDirectory = null,
        string? indexFilePath = null,
        string? ompExecutablePath = null,
        bool disableWindowScan = false,
        bool disableAutoRefresh = false,
        bool redactFilePath = false)
    {
        AgentDirectory = NormalizeOptionalPath(agentDirectory);
        IndexFilePath = NormalizeOptionalPath(indexFilePath);
        OmpExecutablePath = NormalizeOptionalPath(ompExecutablePath);
        DisableWindowScan = disableWindowScan;
        DisableAutoRefresh = disableAutoRefresh;
        RedactFilePath = redactFilePath;
    }

    /// <summary>
    /// 解析受支持的命令行参数；遇到未知参数或缺少值时抛出 <see cref="ArgumentException"/>，
    /// 由应用入口转换为面向用户的错误提示。
    /// </summary>
    public static AppLaunchOptions Parse(IReadOnlyList<string>? arguments)
    {
        var values = arguments ?? Array.Empty<string>();
        string? agentDirectory = null;
        string? indexFilePath = null;
        string? ompExecutablePath = null;
        var disableWindowScan = false;
        var disableAutoRefresh = false;
        var redactFilePath = false;

        for (var index = 0; index < values.Count; index++)
        {
            switch (values[index])
            {
                case AgentDirectoryOption:
                    agentDirectory = ReadOptionValue(values, ref index, AgentDirectoryOption);
                    break;

                case IndexFileOption:
                    indexFilePath = ReadOptionValue(values, ref index, IndexFileOption);
                    break;

                case OmpExecutableOption:
                    ompExecutablePath = ReadOptionValue(values, ref index, OmpExecutableOption);
                    break;

                case DisableWindowScanOption:
                    disableWindowScan = true;
                    break;

                case DisableAutoRefreshOption:
                    disableAutoRefresh = true;
                    break;

                case RedactFilePathOption:
                    redactFilePath = true;
                    break;

                default:
                    throw new ArgumentException($"不支持的启动参数：{values[index]}");
            }
        }

        return new AppLaunchOptions(
            agentDirectory,
            indexFilePath,
            ompExecutablePath,
            disableWindowScan,
            disableAutoRefresh,
            redactFilePath);
    }

    /// <summary>
    /// 读取需要路径值的参数，并拒绝缺失或看似另一个开关的值，避免错误地写入默认索引文件。
    /// </summary>
    private static string ReadOptionValue(IReadOnlyList<string> arguments, ref int index, string optionName)
    {
        if (index + 1 >= arguments.Count
            || string.IsNullOrWhiteSpace(arguments[index + 1])
            || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"启动参数 {optionName} 需要一个路径值。");
        }

        index++;
        return arguments[index];
    }

    /// <summary>将空白路径还原为 <see langword="null"/>，使调用方继续使用安全的默认位置。</summary>
    private static string? NormalizeOptionalPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
    }
}
