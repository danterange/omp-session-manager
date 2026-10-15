using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OmpSessionManager.Services;

/// <summary>
/// 描述一个当前用户桌面中的命令行顶层窗口，用于匹配 OMP 会话标题。
/// </summary>
public sealed record OmpWindowInfo(
    int ProcessId,
    IntPtr WindowHandle,
    string WindowTitle,
    string ProcessName,
    string ProcessPath);

/// <summary>
/// 通过 Win32 枚举当前 Windows 登录 session 中的任务栏命令行窗口。
/// </summary>
public sealed class OmpWindowScanner
{
    private static readonly HashSet<string> CommandLineHostNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd",
        "conhost",
        "pwsh",
        "powershell",
        "WindowsTerminal",
        "mintty"
    };

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr lParam);

    /// <summary>
    /// 获取当前用户可见且带标题的命令行顶层窗口，排除其他用户窗口和无标题窗口。
    /// </summary>
    public IReadOnlyList<OmpWindowInfo> Scan()
    {
        using var currentProcess = Process.GetCurrentProcess();
        var currentSessionId = currentProcess.SessionId;
        var windows = new List<OmpWindowInfo>();

        EnumWindows((windowHandle, _) =>
        {
            if (!IsWindowVisible(windowHandle))
            {
                return true;
            }

            var windowTitle = GetWindowTitle(windowHandle);
            if (windowTitle.Length == 0)
            {
                return true;
            }

            var processId = GetWindowProcessId(windowHandle);
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.SessionId != currentSessionId
                    || !CommandLineHostNames.Contains(process.ProcessName))
                {
                    return true;
                }

                windows.Add(new OmpWindowInfo(
                    process.Id,
                    windowHandle,
                    windowTitle,
                    process.ProcessName,
                    TryGetProcessPath(process)));
            }
            catch (ArgumentException)
            {
                // 窗口所属进程可能在读取期间退出，跳过本次刷新中的失效窗口。
            }
            catch (InvalidOperationException)
            {
                // 进程退出或属性不可用时不影响其他窗口扫描。
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 受保护进程的属性不可读时不影响其他窗口扫描。
            }

            return true;
        }, IntPtr.Zero);

        return windows
            .GroupBy(window => window.WindowHandle)
            .Select(group => group.First())
            .OrderBy(window => window.ProcessId)
            .ToList();
    }

    /// <summary>读取窗口标题；空标题返回空字符串供调用方过滤。</summary>
    private static string GetWindowTitle(IntPtr windowHandle)
    {
        var length = GetWindowTextLength(windowHandle);
        if (length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(windowHandle, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    /// <summary>读取窗口关联的进程 ID。</summary>
    private static int GetWindowProcessId(IntPtr windowHandle)
    {
        GetWindowThreadProcessId(windowHandle, out var processId);
        return unchecked((int)processId);
    }

    /// <summary>读取进程路径；权限不足或进程退出时返回空字符串。</summary>
    private static string TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maxCount);
}
