using System.Text.Json;
using OmpSessionManager.Models;

namespace OmpSessionManager.Services;

/// <summary>
/// 扫描 OMP JSONL 会话文件，并提取恢复所需的稳定元数据。
/// </summary>
public sealed class OmpSessionScanner
{
    private const string SessionDirectoryName = "sessions";
    private const string SessionFilePattern = "*.jsonl";
    private const int MaxSummaryLength = 180;

    /// <summary>
    /// 枚举当前 OMP profile 下的会话文件，跳过格式损坏或缺少 ID 的文件。
    /// </summary>
    public IReadOnlyList<SessionRecord> Scan(string? agentDirectory = null)
    {
        var root = agentDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".omp",
            "agent");
        var sessionRoot = Path.Combine(root, SessionDirectoryName);

        if (!Directory.Exists(sessionRoot))
        {
            return Array.Empty<SessionRecord>();
        }

        var records = new List<SessionRecord>();
        foreach (var filePath in Directory.EnumerateFiles(sessionRoot, SessionFilePattern, SearchOption.AllDirectories))
        {
            var record = TryReadSession(filePath);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return records
            .GroupBy(record => record.SessionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(record => record.LastSeenAtUtc).First())
            .OrderByDescending(record => record.LastSeenAtUtc)
            .ToList();
    }

    /// <summary>
    /// 从单个 JSONL 文件读取标题、会话 ID、工作目录和首条用户消息，并允许 OMP 持续写入该文件。
    /// </summary>
    private static SessionRecord? TryReadSession(string filePath)
    {
        try
        {
            var isFileInUse = IsFileInUse(filePath);
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var firstLine = reader.ReadLine();
            var secondLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(secondLine))
            {
                return null;
            }

            using var sessionDocument = JsonDocument.Parse(secondLine);
            var sessionElement = sessionDocument.RootElement;
            if (!TryGetStringProperty(sessionElement, "id", out var sessionId)
                || string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            var fileInfo = new FileInfo(filePath);
            var title = ReadStringProperty(firstLine, "title") ?? "未命名会话";
            var workingDirectory = ReadStringProperty(secondLine, "cwd") ?? string.Empty;
            var createdAt = ReadDateTimeProperty(secondLine, "timestamp") ?? fileInfo.CreationTimeUtc;
            var summary = ReadFirstUserMessage(reader) ?? "暂无用户消息摘要";

            return new SessionRecord
            {
                SessionId = sessionId,
                Title = title,
                Summary = NormalizeSummary(summary),
                WorkingDirectory = workingDirectory,
                SessionFilePath = filePath,
                CreatedAtUtc = createdAt,
                LastSeenAtUtc = fileInfo.LastWriteTimeUtc,
                FirstSeenAtUtc = fileInfo.CreationTimeUtc,
                IsFileInUse = isFileInUse
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // JSON 合法但字段类型不符合预期时跳过本文件，避免异常穿透 WinForms 刷新事件。
            return null;
        }
    }

    /// <summary>
    /// 通过默认共享模式探测 OMP 是否仍持有写入句柄；探测失败时不阻止后续共享读取。
    /// </summary>
    private static bool IsFileInUse(string filePath)
    {
        try
        {
            using var probe = File.OpenRead(filePath);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>
    /// 顺序查找首条用户文本，以便列表摘要代表真实任务而不是系统事件。
    /// </summary>
    private static string? ReadFirstUserMessage(StreamReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!TryGetStringProperty(root, "type", out var recordType)
                    || !string.Equals(recordType, "message", StringComparison.Ordinal)
                    || root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("message", out var messageElement)
                    || !TryGetStringProperty(messageElement, "role", out var role)
                    || !string.Equals(role, "user", StringComparison.Ordinal)
                    || messageElement.ValueKind != JsonValueKind.Object
                    || !messageElement.TryGetProperty("content", out var contentElement))
                {
                    continue;
                }

                if (contentElement.ValueKind == JsonValueKind.String)
                {
                    return contentElement.GetString();
                }

                if (contentElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in contentElement.EnumerateArray())
                {
                    if (TryGetStringProperty(item, "type", out var itemType)
                        && string.Equals(itemType, "text", StringComparison.Ordinal)
                        && TryGetStringProperty(item, "text", out var text))
                    {
                        return text;
                    }
                }
            }
            catch (JsonException)
            {
                // 会话可能正被 OMP 追加写入；只跳过当前不完整行，继续读取已有内容。
            }
            catch (InvalidOperationException)
            {
                // 字段可能由未来版本改为其他 JSON 类型；只跳过当前行以保留可恢复会话记录。
            }
        }

        return null;
    }

    /// <summary>
    /// 从 JSON 行读取字符串属性；不符合预期时返回空值。
    /// </summary>
    private static string? ReadStringProperty(string? jsonLine, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(jsonLine);
            return TryGetStringProperty(document.RootElement, propertyName, out var value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// 仅从对象中的字符串字段取值；JSON 语法正确但类型变化时返回 <see langword="false"/>，
    /// 防止 <see cref="JsonElement.GetString"/> 在扫描路径中抛出异常。
    /// </summary>
    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return value is not null;
    }

    /// <summary>
    /// 从 JSON 行读取 ISO 8601 时间；非法值返回空值并由调用方回退到文件时间。
    /// </summary>
    private static DateTimeOffset? ReadDateTimeProperty(string jsonLine, string propertyName)
    {
        var value = ReadStringProperty(jsonLine, propertyName);
        return DateTimeOffset.TryParse(value, out var timestamp) ? timestamp : null;
    }

    /// <summary>
    /// 清理摘要中的换行与过长文本，保证列表行高度稳定且不泄露完整对话内容。
    /// </summary>
    private static string NormalizeSummary(string summary)
    {
        var normalized = string.Join(
            " ",
            summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= MaxSummaryLength
            ? normalized
            : string.Concat(normalized.AsSpan(0, MaxSummaryLength - 1), "…");
    }
}
