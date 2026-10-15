using OmpSessionManager.Models;

namespace OmpSessionManager.Services;

/// <summary>
/// 根据任务栏命令行窗口标题匹配当前打开的 OMP 会话。
/// </summary>
public sealed class OmpWindowMatcher
{
    /// <summary>
    /// 优先用完整会话标题匹配窗口；标题缺失时，仅用唯一活动会话的工作目录名作为回退匹配。
    /// </summary>
    public IReadOnlySet<string> MatchSessionIds(
        IEnumerable<SessionRecord> sessions,
        IEnumerable<OmpWindowInfo> windows)
    {
        var sessionList = sessions
            .Where(session => !string.IsNullOrWhiteSpace(session.SessionId))
            .ToList();
        var windowList = windows
            .Select(window => new WindowCandidate(window, NormalizeTitle(window.WindowTitle)))
            .Where(candidate => candidate.NormalizedTitle.Length > 0)
            .ToList();
        var matchedSessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. 标题是 OMP 最稳定的用户可见标识；优先完成精确的包含匹配。
        foreach (var window in windowList)
        {
            foreach (var session in sessionList.Where(session => MatchesTitle(window.NormalizedTitle, session.Title)))
            {
                matchedSessionIds.Add(session.SessionId);
            }
        }

        // 2. 有些 OMP 窗口标题只显示工作目录。仅对唯一、仍被写入且尚未命中的会话使用目录名回退。
        foreach (var window in windowList)
        {
            var workspaceName = ExtractWindowSubject(window.NormalizedTitle);
            var candidates = sessionList
                .Where(session => session.IsFileInUse
                    && !matchedSessionIds.Contains(session.SessionId)
                    && string.Equals(
                        GetWorkspaceName(session.WorkingDirectory),
                        workspaceName,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 1)
            {
                matchedSessionIds.Add(candidates[0].SessionId);
            }
        }

        return matchedSessionIds;
    }

    /// <summary>判断终端窗口标题是否包含完整 OMP 会话标题。</summary>
    private static bool MatchesTitle(string windowTitle, string? sessionTitle)
    {
        var normalizedSessionTitle = NormalizeTitle(sessionTitle);
        return normalizedSessionTitle.Length > 0
            && windowTitle.Contains(normalizedSessionTitle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>移除 Windows Terminal 的 OMP 前缀，得到用于目录名回退的主体文本。</summary>
    private static string ExtractWindowSubject(string windowTitle)
    {
        var markerIndex = windowTitle.IndexOf(" > ", StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            return windowTitle[(markerIndex + 3)..];
        }

        markerIndex = windowTitle.IndexOf(" : ", StringComparison.Ordinal);
        return markerIndex >= 0 ? windowTitle[(markerIndex + 3)..] : windowTitle;
    }

    /// <summary>从工作目录中提取最后一级目录名；空或无效路径不参与目录名回退。</summary>
    private static string GetWorkspaceName(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFileName(workingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (NotSupportedException)
        {
            return string.Empty;
        }
    }

    /// <summary>压缩标题中的重复空白，降低终端标题格式差异对匹配结果的影响。</summary>
    private static string NormalizeTitle(string? title)
    {
        return string.Join(
            " ",
            (title ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>保存标准化后的窗口标题，避免匹配阶段重复进行字符串归一化。</summary>
    private sealed record WindowCandidate(OmpWindowInfo Window, string NormalizedTitle);
}
