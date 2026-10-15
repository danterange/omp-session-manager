using System.Text.Json;
using System.Text.Json.Serialization;
using OmpSessionManager.Models;

namespace OmpSessionManager.Services;

/// <summary>
/// 描述会话管理器自身的持久化索引；不包含 OMP 原始对话内容。
/// </summary>
public sealed class SessionIndex
{
    /// <summary>获取或设置当前管理器记录的会话集合。</summary>
    public List<SessionRecord> Sessions { get; set; } = new();
}

/// <summary>
/// 负责跨重启保存会话记录，并维护用户的移除与未完成状态。
/// </summary>
public sealed class SessionStore
{
    private readonly string indexFilePath;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 使用用户本地 AppData 下的固定索引文件创建存储器，避免污染 OMP 数据目录。
    /// </summary>
    public SessionStore(string? indexFilePath = null)
    {
        this.indexFilePath = indexFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OmpSessionManager",
            "sessions.json");
    }

    /// <summary>
    /// 读取索引；首次运行或索引不存在时返回空索引。
    /// </summary>
    public SessionIndex Load()
    {
        if (!File.Exists(indexFilePath))
        {
            return new SessionIndex();
        }

        try
        {
            var json = File.ReadAllText(indexFilePath);
            var index = JsonSerializer.Deserialize<SessionIndex>(json, jsonOptions) ?? new SessionIndex();
            index.Sessions = index.Sessions?
                .Where(record => record is not null)
                .Select(NormalizeRecord)
                .ToList()
                ?? new List<SessionRecord>();
            return index;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"会话索引格式无效：{indexFilePath}", exception);
        }
        catch (IOException exception)
        {
            throw new IOException($"无法读取会话索引：{indexFilePath}", exception);
        }
    }

    /// <summary>
    /// 将来自可编辑 JSON 的空字符串字段规范为安全默认值，避免旧索引或手工编辑内容破坏搜索和窗口匹配。
    /// </summary>
    private static SessionRecord NormalizeRecord(SessionRecord record)
    {
        record.SessionId ??= string.Empty;
        record.Title ??= string.Empty;
        record.Summary ??= string.Empty;
        record.WorkingDirectory ??= string.Empty;
        record.SessionFilePath ??= string.Empty;
        return record;
    }

    /// <summary>
    /// 将扫描发现的会话合并进索引，同时保留用户已经移除和标记未完成的状态。
    /// </summary>
    public SessionIndex MergeDiscovered(SessionIndex index, IReadOnlyList<SessionRecord> discovered)
    {
        index.Sessions ??= new List<SessionRecord>();
        var existingById = index.Sessions
            .Where(record => !string.IsNullOrWhiteSpace(record.SessionId))
            .GroupBy(record => record.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var current in discovered)
        {
            if (existingById.TryGetValue(current.SessionId, out var existing))
            {
                existing.Title = current.Title;
                existing.Summary = current.Summary;
                existing.WorkingDirectory = current.WorkingDirectory;
                existing.SessionFilePath = current.SessionFilePath;
                existing.CreatedAtUtc = current.CreatedAtUtc;
                existing.LastSeenAtUtc = current.LastSeenAtUtc;
                existing.FirstSeenAtUtc = existing.FirstSeenAtUtc == default
                    ? current.FirstSeenAtUtc
                    : existing.FirstSeenAtUtc;
                existing.IsFileInUse = current.IsFileInUse;
                continue;
            }

            index.Sessions.Add(current);
            existingById[current.SessionId] = current;
        }

        return index;
    }

    /// <summary>
    /// 将索引安全写入临时文件后替换目标文件，避免程序中断时留下半份 JSON。
    /// </summary>
    public void Save(SessionIndex index)
    {
        var directory = Path.GetDirectoryName(indexFilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("会话索引路径没有有效目录。");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = $"{indexFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(index, jsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, indexFilePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    /// 标记指定会话为已移除；只影响管理器索引，不删除 OMP 原始文件。
    /// </summary>
    public void MarkRemoved(SessionIndex index, string sessionId, bool removed)
    {
        var session = index.Sessions.FirstOrDefault(
            record => string.Equals(record.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        if (session is null)
        {
            return;
        }

        session.IsRemoved = removed;
        Save(index);
    }

    /// <summary>
    /// 批量设置未完成标签并只保存一次，避免逐条操作造成重复文件写入。
    /// </summary>
    public int MarkIncomplete(SessionIndex index, IEnumerable<string> sessionIds, bool incomplete)
    {
        var idSet = sessionIds
            .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changedCount = 0;

        foreach (var session in index.Sessions)
        {
            if (!idSet.Contains(session.SessionId) || session.IsIncomplete == incomplete)
            {
                continue;
            }

            session.IsIncomplete = incomplete;
            changedCount++;
        }

        if (changedCount > 0)
        {
            Save(index);
        }

        return changedCount;
    }
}
