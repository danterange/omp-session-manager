using OmpSessionManager.Models;
using OmpSessionManager.Services;

namespace OmpSessionManager.Tests;

/// <summary>验证 OMP 可执行文件的解析优先级，不启动真实 OMP 进程。</summary>
[TestClass]
public sealed class OmpLauncherTests
{
    private string temporaryRoot = string.Empty;

    /// <summary>为每个可执行文件解析测试创建独立目录，避免依赖真实 PATH 或用户安装。</summary>
    [TestInitialize]
    public void SetUp()
    {
        temporaryRoot = Path.Combine(Path.GetTempPath(), $"OmpLauncherTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
    }

    /// <summary>删除测试生成的空占位文件，避免临时目录累积无关发布物。</summary>
    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(temporaryRoot))
        {
            Directory.Delete(temporaryRoot, true);
        }
    }

    /// <summary>确认用户显式指定的现有可执行文件优先于默认位置和 PATH。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void ResolveExecutablePath_PrefersExplicitExistingFile()
    {
        var executablePath = Path.Combine(temporaryRoot, "omp.exe");
        File.WriteAllText(executablePath, "fixture");
        var launcher = new OmpLauncher(executablePath, () => string.Empty, () => string.Empty);

        var resolvedPath = launcher.ResolveExecutablePath();

        Assert.AreEqual(executablePath, resolvedPath);
    }

    /// <summary>确认默认安装位置缺失时会继续搜索 PATH，而不是错误地拒绝已安装的 OMP。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void ResolveExecutablePath_FallsBackToPath()
    {
        var executablePath = Path.Combine(temporaryRoot, "omp.exe");
        File.WriteAllText(executablePath, "fixture");
        var launcher = new OmpLauncher(
            null,
            () => Path.Combine(temporaryRoot, "missing-default.exe"),
            () => temporaryRoot);

        var resolvedPath = launcher.ResolveExecutablePath();

        Assert.AreEqual(executablePath, resolvedPath);
    }

    /// <summary>确认缺失的显式路径会在启动前报错，不会错误地尝试恢复会话。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Start_RejectsMissingExplicitExecutable()
    {
        var launcher = new OmpLauncher(
            Path.Combine(temporaryRoot, "missing.exe"),
            () => string.Empty,
            () => string.Empty);
        var session = new SessionRecord { SessionId = "safe-session", WorkingDirectory = temporaryRoot };

        Assert.ThrowsException<FileNotFoundException>(() => launcher.Start(session));
    }
}
