using OmpSessionManager.Models;

namespace OmpSessionManager.Services;

/// <summary>
/// 按标题或内容简介筛选会话，支持空白分隔的多关键词包含匹配。
/// </summary>
public sealed class SessionSearch
{
    /// <summary>
    /// 返回所有关键词都命中标题或简介任一字段的会话；空查询返回全部会话。
    /// </summary>
    public IReadOnlyList<SessionRecord> Filter(IEnumerable<SessionRecord> sessions, string? query)
    {
        var keywords = Tokenize(query);
        if (keywords.Length == 0)
        {
            return sessions.ToList();
        }

        return sessions
            .Where(session => keywords.All(keyword =>
                (session.Title ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || (session.Summary ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>
    /// 将查询拆成关键词，兼容星号包含写法并忽略多余空白。
    /// </summary>
    private static string[] Tokenize(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<string>();
        }

        return query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(keyword => keyword.Trim('*'))
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
