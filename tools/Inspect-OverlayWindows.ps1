# Read-only diagnostic for visible and hidden windows owned by CodexCacheTimer.
param([int]$ProcessId = 0)

if (-not $ProcessId) {
    $candidate = Get-Process -Name CodexCacheTimer -ErrorAction SilentlyContinue |
        Sort-Object StartTime -Descending | Select-Object -First 1
    if (-not $candidate) { throw 'CodexCacheTimer is not running.' }
    $ProcessId = $candidate.Id
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class OverlayWindowInspector {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    public static List<string> Inspect(int processId) {
        var rows = new List<string>();
        EnumWindows((hwnd, data) => {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid != processId) return true;
            var title = new StringBuilder(256);
            var cls = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            GetClassName(hwnd, cls, cls.Capacity);
            Rect rect;
            GetWindowRect(hwnd, out rect);
            rows.Add(string.Format("hwnd={0} visible={1} owner={2} exstyle=0x{3:X} rect={4},{5},{6},{7} class={8} title={9}",
                hwnd, IsWindowVisible(hwnd), GetWindow(hwnd, 4), GetWindowLongPtr(hwnd, -20).ToInt64(),
                rect.Left, rect.Top, rect.Right, rect.Bottom, cls, title));
            return true;
        }, IntPtr.Zero);
        return rows;
    }
}
'@

[OverlayWindowInspector]::Inspect($ProcessId)
