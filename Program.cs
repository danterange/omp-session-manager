namespace OmpSessionManager;

static class Program
{
    /// <summary>
    /// 初始化 WinForms 并解析隔离运行参数；参数无效时显示不含本地路径的说明，而不是让应用在启动时崩溃。
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            Application.Run(new Form1(AppLaunchOptions.Parse(args)));
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(
                exception.Message,
                "启动参数无效",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
