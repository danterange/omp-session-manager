using System.Runtime.ExceptionServices;

namespace OmpSessionManager.Tests;

/// <summary>在 STA 线程创建主窗体，验证关键自动化标识可被未来端到端 UI 测试稳定定位。</summary>
[TestClass]
public sealed class FormUiSmokeTests
{
    /// <summary>
    /// 验证主窗体可在隔离配置下构造关键控件，不显示窗体，因此不会扫描真实 OMP profile 或写入用户索引。
    /// </summary>
    [TestMethod]
    [TestCategory("UI")]
    public void Form1_CreatesStableAutomationTargetsOnStaThread()
    {
        Exception? capturedException = null;
        var testThread = new Thread(() =>
        {
            try
            {
                using var form = new Form1(new AppLaunchOptions(disableWindowScan: true, disableAutoRefresh: true));
                form.CreateControl();

                Assert.AreEqual("OMP 会话恢复器", form.Text);
                Assert.AreEqual(1, form.Controls.Find("SessionsGrid", true).Length);
                Assert.AreEqual(1, form.Controls.Find("SearchTextBox", true).Length);
                Assert.AreEqual(1, form.Controls.Find("RestoreIncompleteButton", true).Length);
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        })
        {
            IsBackground = true
        };
        testThread.SetApartmentState(ApartmentState.STA);
        testThread.Start();
        testThread.Join();

        if (capturedException is not null)
        {
            ExceptionDispatchInfo.Capture(capturedException).Throw();
        }
    }
}
