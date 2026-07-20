using System.Runtime.InteropServices;
using System.Text;

namespace BlackSunCyber.ClientAgent;

/// <summary>
/// Detectează dacă utilizatorul este pe desktop (fără altă aplicație în focus).
/// Widget-ul apare doar pe desktop sau când cabinetul widget e activ.
/// </summary>
internal static class ForegroundMonitorHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private static readonly HashSet<string> DesktopClassNames = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW"
    };

    public static bool ShouldShowSessionWidget()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId)
            return true;

        var className = GetWindowClassName(hwnd);
        return DesktopClassNames.Contains(className);
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        _ = GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
