using OmpSessionManager.Models;
using OmpSessionManager.Services;
using System.Text.Json;

namespace OmpSessionManager.Tests;

/// <summary>
/// 验证 OMP 会话扫描、搜索和本地索引的核心可观察行为。
/// </summary>
[TestClass]
public sealed class SessionServiceTests
{
    private string temporaryRoot = string.Empty;
    private string fixtureWorkingDirectory = string.Empty;

    /// <summary>为每个测试创建隔离的临时 OMP 目录。</summary>
    [TestInitialize]
    public void SetUp()
    {
        temporaryRoot = Path.Combine(Path.GetTempPath(), $"OmpSessionManagerTests-{Guid.NewGuid():N}");
        fixtureWorkingDirectory = Path.Combine(temporaryRoot, "ExampleWorkspace");
        Directory.CreateDirectory(temporaryRoot);
    }

    /// <summary>删除本测试创建的临时文件，避免测试内容污染用户目录。</summary>
    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(temporaryRoot))
        {
            Directory.Delete(temporaryRoot, true);
        }
    }

    /// <summary>确认扫描器能从真实 JSONL 结构提取 ID、标题、工作目录和用户摘要。</summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void Scan_ExtractsSessionMetadataAndSummary()
    {
        var sessionFile = CreateSessionFile();
        var scanner = new OmpSessionScanner();

        var records = scanner.Scan(temporaryRoot);

        Assert.AreEqual(1, records.Count);
        Assert.AreEqual("smoke-session-001", records[0].SessionId);
        Assert.AreEqual("夹具会话标题", records[0].Title);
        Assert.AreEqual(fixtureWorkingDirectory, records[0].WorkingDirectory);
        Assert.AreEqual("验证 JSONL 摘要提取", records[0].Summary);
        Assert.AreEqual(sessionFile, records[0].SessionFilePath);
    }

    /// <summary>确认 OMP 正在共享写入会话文件时，扫描器仍可读取已完成的 JSONL 记录。</summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void Scan_ReadsSessionWhileWriterSharesFile()
    {
        var sessionFile = CreateSessionFile();
        using var writer = new FileStream(sessionFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

        var records = new OmpSessionScanner().Scan(temporaryRoot);

        Assert.AreEqual(1, records.Count);
        Assert.AreEqual("smoke-session-001", records[0].SessionId);
        Assert.IsTrue(records[0].IsFileInUse);
    }

    /// <summary>确认移除和未完成状态保存后重新加载仍保留，重新发现不会覆盖标签。</summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void Store_PreservesRemovedAndIncompleteStateAcrossReloadAndMerge()
    {
        var indexPath = Path.Combine(temporaryRoot, "index.json");
        var session = new SessionRecord
        {
            SessionId = "smoke-session-002",
            Title = "原始标题",
            Summary = "原始摘要",
            WorkingDirectory = fixtureWorkingDirectory,
            IsIncomplete = true
        };
        var store = new SessionStore(indexPath);
        var index = new SessionIndex { Sessions = new List<SessionRecord> { session } };

        store.Save(index);
        store.MarkRemoved(index, session.SessionId, true);
        var discovered = new SessionRecord
        {
            SessionId = session.SessionId,
            Title = "更新标题",
            Summary = "更新摘要",
            WorkingDirectory = session.WorkingDirectory,
            LastSeenAtUtc = DateTimeOffset.UtcNow
        };
        store.MergeDiscovered(index, new[] { discovered });
        store.Save(index);

        var reloaded = new SessionStore(indexPath).Load();

        Assert.AreEqual(1, reloaded.Sessions.Count);
        Assert.IsTrue(reloaded.Sessions[0].IsRemoved);
        Assert.IsTrue(reloaded.Sessions[0].IsIncomplete);
        Assert.AreEqual("更新标题", reloaded.Sessions[0].Title);
    }

    /// <summary>
    /// 确认语法合法但字段类型异常的 JSONL 不会使扫描器抛出异常，并会跳过或回退异常字段。
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void Scan_ToleratesTypeInvalidJsonFields()
    {
        var sessionFile = CreateSessionFile();
        File.WriteAllLines(
            sessionFile,
            new[]
            {
                "{\"type\":\"title\",\"title\":\"异常字段夹具\"}",
                "{\"type\":\"session\",\"id\":42,\"cwd\":[],\"timestamp\":{}}"
            });

        var skippedRecords = new OmpSessionScanner().Scan(temporaryRoot);

        Assert.AreEqual(0, skippedRecords.Count);

        File.WriteAllLines(
            sessionFile,
            new[]
            {
                "{\"type\":\"title\",\"title\":\"异常字段夹具\"}",
                "{\"type\":\"session\",\"id\":\"type-safe-001\",\"cwd\":[],\"timestamp\":{}}",
                "{\"type\":\"message\",\"message\":{\"role\":\"user\",\"content\":[42,{\"type\":17,\"text\":99}]}}"
            });

        var fallbackRecords = new OmpSessionScanner().Scan(temporaryRoot);

        Assert.AreEqual(1, fallbackRecords.Count);
        Assert.AreEqual(string.Empty, fallbackRecords[0].WorkingDirectory);
        Assert.AreEqual("暂无用户消息摘要", fallbackRecords[0].Summary);
    }

    /// <summary>确认任务栏窗口标题包含会话标题时能找到对应 session。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void WindowMatcher_MatchesSessionByWindowTitle()
    {
        var sessions = new[]
        {
            new SessionRecord { SessionId = "open-001", Title = "昨天的 项目 测试" },
            new SessionRecord { SessionId = "closed-002", Title = "另一个会话" }
        };
        var windows = new[]
        {
            new OmpWindowInfo(1234, new IntPtr(1), "Windows Terminal - 昨天的 项目 测试", "WindowsTerminal", "")
        };
        var matcher = new OmpWindowMatcher();

        var matchedIds = matcher.MatchSessionIds(sessions, windows);

        Assert.IsTrue(matchedIds.Contains("open-001"));
        Assert.IsFalse(matchedIds.Contains("closed-002"));
    }
    /// <summary>确认短窗口标题不能反向匹配较长会话标题，避免误标记。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void WindowMatcher_DoesNotMatchWhenWindowTitleIsOnlyShortFragment()
    {
        var sessions = new[]
        {
            new SessionRecord { SessionId = "session-001", Title = "完整的任务标题" }
        };
        var windows = new[]
        {
            new OmpWindowInfo(1234, new IntPtr(1), "任务", "WindowsTerminal", "")
        };

        var matchedIds = new OmpWindowMatcher().MatchSessionIds(sessions, windows);

        Assert.IsFalse(matchedIds.Contains("session-001"));
    }
    /// <summary>确认目录名窗口只匹配唯一且未被标题匹配占用的活动会话。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void WindowMatcher_MatchesUniqueActiveWorkspaceFallback()
    {
        var sessions = new[]
        {
            new SessionRecord
            {
                SessionId = "titled-001",
                Title = "移除仅刷新显示功能",
                WorkingDirectory = fixtureWorkingDirectory,
                IsFileInUse = true
            },
            new SessionRecord
            {
                SessionId = "fallback-002",
                WorkingDirectory = fixtureWorkingDirectory,
                IsFileInUse = true
            }
        };
        var windows = new[]
        {
            new OmpWindowInfo(1, new IntPtr(1), "π > 移除仅刷新显示功能", "WindowsTerminal", string.Empty),
            new OmpWindowInfo(2, new IntPtr(2), "π > ExampleWorkspace", "WindowsTerminal", string.Empty)
        };

        var matchedIds = new OmpWindowMatcher().MatchSessionIds(sessions, windows);

        CollectionAssert.AreEquivalent(new[] { "titled-001", "fallback-002" }, matchedIds.ToArray());
    }

    /// <summary>确认同一目录存在多个未匹配活动会话时不进行猜测性目录名匹配。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void WindowMatcher_SkipsAmbiguousActiveWorkspaceFallback()
    {
        var sessions = new[]
        {
            new SessionRecord { SessionId = "first-001", WorkingDirectory = fixtureWorkingDirectory, IsFileInUse = true },
            new SessionRecord { SessionId = "second-002", WorkingDirectory = fixtureWorkingDirectory, IsFileInUse = true }
        };
        var windows = new[]
        {
            new OmpWindowInfo(1, new IntPtr(1), "π > ExampleWorkspace", "WindowsTerminal", string.Empty)
        };

        var matchedIds = new OmpWindowMatcher().MatchSessionIds(sessions, windows);

        Assert.AreEqual(0, matchedIds.Count);
    }


    /// <summary>确认旧版索引缺少未完成字段时按未标记兼容读取。</summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void Store_LoadsLegacyIndexWithIncompleteDefaultFalse()
    {
        var indexPath = Path.Combine(temporaryRoot, "legacy-index.json");
        File.WriteAllText(indexPath, "{\"Sessions\":[{\"SessionId\":\"legacy-001\",\"Title\":\"旧记录\"}]}");

        var index = new SessionStore(indexPath).Load();

        Assert.AreEqual(1, index.Sessions.Count);
        Assert.IsFalse(index.Sessions[0].IsIncomplete);
    }

    /// <summary>
    /// 确认可编辑索引中出现 null 记录或 null 字段时，读取会规范化为安全空值而不会破坏后续搜索。
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void Store_Load_NormalizesNullRecordsAndFields()
    {
        var indexPath = Path.Combine(temporaryRoot, "null-fields-index.json");
        File.WriteAllText(
            indexPath,
            "{\"Sessions\":[null,{\"SessionId\":null,\"Title\":null,\"Summary\":null,\"WorkingDirectory\":null,\"SessionFilePath\":null}]}");

        var index = new SessionStore(indexPath).Load();
        var results = new SessionSearch().Filter(index.Sessions, "safe-query");

        Assert.AreEqual(1, index.Sessions.Count);
        Assert.AreEqual(string.Empty, index.Sessions[0].SessionId);
        Assert.AreEqual(string.Empty, index.Sessions[0].Title);
        Assert.AreEqual(0, results.Count);
    }

    /// <summary>确认旧索引标题为空时，合并扫描结果后同次窗口匹配仍能命中会话。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Store_MergesTitleBeforeWindowMatching()
    {
        var indexPath = Path.Combine(temporaryRoot, "merge-title-index.json");
        var index = new SessionIndex
        {
            Sessions = new List<SessionRecord>
            {
                new() { SessionId = "open-001", Title = string.Empty }
            }
        };
        var discovered = new SessionRecord
        {
            SessionId = "open-001",
            Title = "已打开的 OMP 会话",
            Summary = "摘要"
        };
        var store = new SessionStore(indexPath);

        store.MergeDiscovered(index, new[] { discovered });
        var matchedIds = new OmpWindowMatcher().MatchSessionIds(
            index.Sessions,
            new[]
            {
                new OmpWindowInfo(1234, new IntPtr(1), "π : 已打开的 OMP 会话", "WindowsTerminal", string.Empty)
            });

        Assert.IsTrue(matchedIds.Contains("open-001"));
    }

    /// <summary>确认非法工作目录文本不会让窗口匹配在路径解析阶段抛出异常。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void WindowMatcher_SkipsInvalidWorkspacePath()
    {
        var sessions = new[]
        {
            new SessionRecord { SessionId = "invalid-path-001", WorkingDirectory = "invalid\0path", IsFileInUse = true }
        };
        var windows = new[]
        {
            new OmpWindowInfo(1, new IntPtr(1), "π > ExampleWorkspace", "WindowsTerminal", string.Empty)
        };

        var matchedIds = new OmpWindowMatcher().MatchSessionIds(sessions, windows);

        Assert.AreEqual(0, matchedIds.Count);
    }




    /// <summary>确认每个关键词命中标题或简介任一字段，且多个关键词仍按 AND 处理。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Search_RequiresEveryKeywordInTitleOrSummary()
    {
        var titleMatch = new SessionRecord
        {
            Title = "标题专用关键词 项目",
            Summary = "普通描述"
        };
        var summaryMatch = new SessionRecord
        {
            Title = "其他项目",
            Summary = "摘要专用关键词"
        };
        var incompleteMatch = new SessionRecord
        {
            Title = "标题专用关键词",
            Summary = "普通描述"
        };
        var search = new SessionSearch();

        var singleKeywordResults = search.Filter(
            new[] { titleMatch, summaryMatch },
            "专用关键词");
        var multiKeywordResults = search.Filter(
            new[] { titleMatch, summaryMatch, incompleteMatch },
            "专用关键词 项目");

        Assert.AreEqual(2, singleKeywordResults.Count);
        CollectionAssert.AreEquivalent(new[] { titleMatch, summaryMatch }, singleKeywordResults.ToArray());
        Assert.AreEqual(2, multiKeywordResults.Count);
        CollectionAssert.AreEquivalent(new[] { titleMatch, summaryMatch }, multiKeywordResults.ToArray());
    }

    /// <summary>创建最小但符合 OMP 当前格式的 JSONL 会话文件。</summary>
    private string CreateSessionFile()
    {
        var directory = Path.Combine(temporaryRoot, "sessions", "-smoke");
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "sample.jsonl");
        var lines = new[]
        {
            "{\"type\":\"title\",\"title\":\"夹具会话标题\"}",
            JsonSerializer.Serialize(new
            {
                type = "session",
                id = "smoke-session-001",
                cwd = fixtureWorkingDirectory,
                timestamp = "2026-01-01T01:02:03.000Z",
                title = "夹具会话标题"
            }),
            "{\"type\":\"message\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"验证 JSONL 摘要提取\"}]}}"
        };
        File.WriteAllLines(filePath, lines);
        return filePath;
    }
}
