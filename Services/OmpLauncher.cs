using System.Diagnostics;
using OmpSessionManager.Models;

namespace OmpSessionManager.Services;

/// <summary>
/// 启动 OMP 并恢复指定会话，避免依赖控制台窗口输入注入。
/// </summary>
public sealed class OmpLauncher
{
    private const string OmpExecutableName = "omp.exe";
    private readonly string? configuredExecutablePath;
    private readonly Func<string> defaultPathProvider;
    private readonly Func<string?> pathProvider;

    /// <summary>
    /// 使用显式路径、默认安装位置或 PATH 中的 omp.exe 创建启动器；可注入 PATH 读取器以便隔离测试。
    /// </summary>
    public OmpLauncher(string? ompExecutablePath = null)
        : this(
            ompExecutablePath,
            () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "omp",
                OmpExecutableName),
            () => Environment.GetEnvironmentVariable("PATH"))
    {
    }

    /// <summary>
    /// 创建可测试的启动器；位置读取器仅用于解析程序位置，不会修改进程环境或用户设置。
    /// </summary>
    internal OmpLauncher(
        string? ompExecutablePath,
        Func<string> defaultPathProvider,
        Func<string?> pathProvider)
    {
        configuredExecutablePath = string.IsNullOrWhiteSpace(ompExecutablePath) ? null : ompExecutablePath.Trim();
        this.defaultPathProvider = defaultPathProvider ?? throw new ArgumentNullException(nameof(defaultPathProvider));
        this.pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    /// <summary>
    /// 使用会话 ID 和原始工作目录启动 OMP 恢复流程。
    /// </summary>
    public Process Start(SessionRecord session)
    {
        if (string.IsNullOrWhiteSpace(session.SessionId))
        {
            throw new ArgumentException("会话 ID 不能为空。", nameof(session));
        }

        var ompExecutablePath = ResolveExecutablePath();

        var startInfo = new ProcessStartInfo
        {
            FileName = ompExecutablePath,
            WorkingDirectory = Directory.Exists(session.WorkingDirectory)
                ? session.WorkingDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add($"--resume={session.SessionId}");

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 OMP 恢复进程。");
    }

    /// <summary>
    /// 按显式参数、默认安装目录、PATH 的顺序定位 OMP，避免安装到非默认目录的用户被错误拒绝。
    /// </summary>
    internal string ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(configuredExecutablePath))
        {
            if (File.Exists(configuredExecutablePath))
            {
                return configuredExecutablePath;
            }

            if (Path.GetDirectoryName(configuredExecutablePath)?.Length == 0)
            {
                var executableFromPath = FindExecutableOnPath(configuredExecutablePath);
                if (executableFromPath is not null)
                {
                    return executableFromPath;
                }
            }

            throw new FileNotFoundException("未找到指定的 OMP 可执行文件。", configuredExecutablePath);
        }

        var defaultExecutablePath = defaultPathProvider();
        if (File.Exists(defaultExecutablePath))
        {
            return defaultExecutablePath;
        }

        var executableFromPathFallback = FindExecutableOnPath(OmpExecutableName);
        if (executableFromPathFallback is not null)
        {
            return executableFromPathFallback;
        }

        throw new FileNotFoundException(
            "未找到 omp.exe。请通过 --omp-executable 指定路径，或将 omp.exe 加入 PATH。",
            OmpExecutableName);
    }

    /// <summary>
    /// 在 PATH 中按目录顺序查找文件名；无效目录项只跳过，避免一项错误阻断后续有效位置。
    /// </summary>
    private string? FindExecutableOnPath(string executableName)
    {
        var pathValue = pathProvider();
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidatePath = Path.Combine(directory.Trim('"'), executableName);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }
            catch (ArgumentException)
            {
                // PATH 条目可由外部安装器写入；坏条目不应妨碍在后续目录查找 OMP。
            }
            catch (NotSupportedException)
            {
                // 非法路径格式只影响当前条目，继续查找其余 PATH 目录。
            }
        }

        return null;
    }
}
