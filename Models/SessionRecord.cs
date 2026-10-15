using System.Text.Json.Serialization;

namespace OmpSessionManager.Models;

/// <summary>
/// 描述一个可由 OMP 恢复的会话及其本地管理状态。
/// </summary>
public sealed class SessionRecord
{
    /// <summary>获取或设置 OMP 会话的稳定 ID。</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>获取或设置 OMP 自动生成或用户可读的标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>获取或设置从首条用户消息提取的内容简介。</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>获取或设置会话启动时使用的工作目录。</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>获取或设置 OMP 原始 JSONL 会话文件路径。</summary>
    public string SessionFilePath { get; set; } = string.Empty;

    /// <summary>获取或设置会话创建时间；无法解析时使用文件时间。</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>获取或设置最近一次从 OMP 文件观察到的更新时间。</summary>
    public DateTimeOffset LastSeenAtUtc { get; set; }

    /// <summary>获取或设置首次被会话管理器发现的时间。</summary>
    public DateTimeOffset FirstSeenAtUtc { get; set; }

    /// <summary>获取或设置用户是否将该会话从主列表移除。</summary>
    public bool IsRemoved { get; set; }

    /// <summary>获取或设置用户是否将该会话标记为后续仍需跟进。</summary>
    public bool IsIncomplete { get; set; }

    /// <summary>
    /// 该字段只用于界面显示，不写入索引；根据当前任务栏窗口标题匹配结果刷新。
    /// </summary>
    [JsonIgnore]
    public bool IsWindowOpen { get; set; }

    /// <summary>
    /// 该字段只用于本次扫描，不写入索引；表示 OMP 仍持有会话文件的写入句柄。
    /// </summary>
    [JsonIgnore]
    public bool IsFileInUse { get; set; }
}
