namespace OmpSessionManager.Tests;

/// <summary>验证隔离运行参数只在显式提供时覆盖默认数据位置。</summary>
[TestClass]
public sealed class AppLaunchOptionsTests
{
    /// <summary>确认路径和两个隔离开关能被完整解析，供演示与 UI 测试复用。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_ReadsIsolatedDataOptions()
    {
        var options = AppLaunchOptions.Parse(
            new[]
            {
                "--agent-directory", "C:\\Example\\agent",
                "--index-file", "C:\\Example\\index.json",
                "--omp-executable", "C:\\Example\\omp.exe",
                "--disable-window-scan",
                "--disable-auto-refresh",
                "--redact-file-path"
            });

        Assert.AreEqual("C:\\Example\\agent", options.AgentDirectory);
        Assert.AreEqual("C:\\Example\\index.json", options.IndexFilePath);
        Assert.AreEqual("C:\\Example\\omp.exe", options.OmpExecutablePath);
        Assert.IsTrue(options.DisableWindowScan);
        Assert.IsTrue(options.DisableAutoRefresh);
        Assert.IsTrue(options.RedactFilePath);
    }

    /// <summary>确认缺少路径值会在启动前被拒绝，不会回退写入用户默认索引。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_RejectsMissingPathValue()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            AppLaunchOptions.Parse(new[] { "--index-file", "--disable-window-scan" }));
    }

    /// <summary>确认未知参数不会被静默忽略，以便用户及时发现脚本或快捷方式配置错误。</summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Parse_RejectsUnknownOption()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            AppLaunchOptions.Parse(new[] { "--unknown-option" }));
    }
}
