# Windows 11 taskbar-gap overlay: research and implementation findings

Updated 2026-09-25. The requested location is the gap immediately left of the hidden-icons chevron (`^`) on the user's Windows 11 taskbar.

## What Windows supports

| Mechanism | Documented behavior | Fit for this timer |
| --- | --- | --- |
| Topmost window | `HWND_TOPMOST` places a window above non-topmost windows and retains that status when deactivated. Other topmost windows may be in front. [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos) | Allows the chosen screen coordinates, without exclusive visibility. |
| Owned popup | An owned window stays above its owner in Z order. `GWLP_HWNDPARENT` sets the owner of a top-level window. [Window Features](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features), [SetWindowLongPtr](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowlongptrw) | Associates the overlay with `Shell_TrayWnd`; it does not reserve taskbar pixels. |
| Tool window | `WS_EX_TOOLWINDOW` excludes the window from the taskbar button list and Alt+Tab. [Extended Window Styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) | Provides the requested Alt+Tab behavior. |
| Notification icon | `Shell_NotifyIcon` manages an icon in the notification area. [The Taskbar](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar) | Supported integration, but not a text slot left of the chevron. |
| Appbar | `SHAppBarMessage` registers a separate screen-edge toolbar and negotiates its rectangle. [Using Application Desktop Toolbars](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars) | Reserves a different screen area, not this taskbar gap. |

The [Windows 11 taskbar configuration documentation](https://learn.microsoft.com/en-us/windows/configuration/customize-taskbar-windows-11) describes supported customization but no arbitrary text slot at this coordinate. The older [deskband documentation](https://learn.microsoft.com/en-us/windows/win32/shell/band-objects) does not establish Windows 11 support for placing a band here. **Inference:** no documented third-party API grants exclusive use of the exact gap. This does not rule out private shell mechanisms.

## Current C# implementation

[`TaskbarOverlayForm.cs`](src/CacheTimer/TaskbarOverlayForm.cs) creates a borderless, topmost WinForms window. Its `CreateParams` set `WS_EX_TOOLWINDOW`, clear `WS_EX_APPWINDOW`, and supply the `Shell_TrayWnd` handle as owner. After showing the form, it verifies and, if needed, repairs that owner with `SetWindowLongPtr(GWLP_HWNDPARENT)`. The overlay is **not** `WS_EX_NOACTIVATE`; it can activate when clicked. It paints the dot and text directly in `OnPaint` using GDI+ and `TextRenderer`. It has no clock hover tooltip.

Placement uses the hidden-icons button's UI Automation bounds and an eight-pixel gap. If the button cannot be found, the code falls back to an approximate position relative to the taskbar's right edge. [`TimerApplicationContext.cs`](src/CacheTimer/TimerApplicationContext.cs) checks the overlay center with `WindowFromPoint` on each one-second clock tick and checks geometry every five seconds. It calls `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE)` only when `GetAncestor` says the hit window belongs to `Shell_TrayWnd`. It does not continuously raise the timer over other applications. [WindowFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-windowfrompoint), [GetAncestor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getancestor), [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)

This is a taskbar-positioned **overlay**, not a component hosted inside Explorer. The popup is a separate form shown above the timer window.

## Live observations and limits

The local diagnostic log at `%LOCALAPPDATA%\CodexCacheTimer\logs\cache-timer-2026-09-25.log` records the native owner, extended styles, visibility, rectangle, paint count, displayed text, and center hit-test result. The user confirmed that taskbar ownership stopped earlier focus-switching disappearance and that the tool-window style kept the timer out of Alt+Tab. The reported dot-only behavior stopped after the child label and form opacity were removed and direct painting was introduced; those simultaneous changes do not isolate a single cause. These are observations on one desktop, not general Windows guarantees.

During a capture at **21:38 local time**, the overlay's center hit changed from itself to a `XamlWindow` owned by the Snipping Tool process for about two seconds. The UI Automation chevron coordinate then changed from `2685` to `2629`, moving the overlay from x=`2551` to x=`2495`. The center hit was `Shell_TrayWnd` for about five seconds. Throughout, the log reported the taskbar as owner, native visibility `True`, advancing timer text, and increasing paint count. The coordinate returned to `2685` and the center hit recovered. This establishes a concurrent hit-test and placement change; it does **not** identify the shell's internal rendering mechanism or prove what pixels were composited at every instant.

In a later run, a conditional `SetWindowPos` call logged `reason=taskbar-covered success=True hit=self`, and the user reported that the timer no longer disappeared in normal use. The user also saw a brief doubled timer during screen capture; available logs do not establish its cause. Other topmost interfaces, including capture surfaces, can still cover the timer. No documented flag makes this overlay permanently outrank all topmost windows. [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)

## Boundaries and further diagnosis

- Keep taskbar ownership, tool-window styling, direct painting, and conditional recovery. Repeated unconditional Z-order changes would compete with foreground tools without providing an exclusive layer.
- Do not use `SetParent` to insert a child window into Explorer. Microsoft documents cross-process DPI-awareness complications and `WS_CHILD`/`WS_POPUP` style responsibilities; it is not a documented taskbar extension point. [SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)
- Explorer restarts, secondary monitors, and auto-hidden taskbars have not had live validation. The app currently reacquires `Shell_TrayWnd` during placement checks, but it has no `TaskbarCreated` message handler or event hook.
- If unexplained coverage recurs, extend the log with the full hit HWND chain, process/class/styles, and neighboring Z-order handles. The current `WindowFromPoint` and root check identify the surface at the center but cannot explain Explorer's internal composition. [GetAncestor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getancestor), [Window Features](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)
