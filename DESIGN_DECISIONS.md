# Codex Cache Timer: design questions and decisions

Updated 2026-09-25. This records the user's answers and the evidence gathered during the design session. The timer describes an **estimate**, never a confirmed cache expiry.

## Purpose and scope

- Help the user budget thinking time before replying to one or two long Codex tasks while their prompt cache may still be warm. It is not a task priority list or a savings dashboard.
- A session means one Codex task/conversation. Forks are separate sessions, even when they share a project or copied history.
- Only user task rollouts count. Codex's background review, subagent, and other internal worker rollouts do not appear as sessions.
- Background subagents are excluded because they are not user-resumable conversations. User-created task forks remain separate rows.
- Version 1 covers local tasks on this Windows PC. No automatic requests to keep a cache warm.
- The desktop companion is implemented in C#.
- Observed cache-hit/miss tracking is deferred.

## Display and interaction

- The always-visible display belongs in the **blank Windows taskbar gap immediately left of the `^` hidden-icons chevron**, on the primary bottom taskbar. It shows only a colored icon and a time. It does not show a model name.
- This requires a taskbar-positioned overlay window; Windows has no documented native arbitrary text slot there. Making the overlay owned by the Explorer taskbar improved visibility while switching apps. The overlay must also stay out of the Alt+Tab switcher.
- Clicking the display opens a popup with all sessions active in the last three hours, including the preferred session. Each row shows a colored icon, the actual Codex title, and a time. A pin icon on the right selects the preferred session. A row click that opens the Codex task is a nice-to-have if simple.
- A newly created task may read `Untitled` until Codex assigns its actual title. Folder name or short ID is not an acceptable permanent substitute.
- Popup order is by the relevant countdown anchor/deadline, with the warmest task at the bottom. Cold tasks remain visible for three hours after their last activity and have a stable place in that same order. The proposed cold label is `Cold · 42m ago`; exact copy is still provisional.
- If no session is estimated warm, show a minimal gray icon, with no expired countdown.
- Colors: green above 10 minutes, amber from 5 to 10 minutes, red below 5 minutes, blue while running, gray when estimated cold.
- While a task runs, show `Running` in the popup and a blue icon with elapsed turn time; after it stops, resume the countdown. The taskbar clock has no hover tooltip.
- No five-minute toast in version 1. Earlier discussion considered an optional persistent toast; the user explicitly removed it to start simple.

## Selection and persistence

- Before a task is manually preferred, display the estimated-warm task that expires soonest.
- A manually preferred task appears whenever it is estimated warm. When it becomes estimated cold, temporarily show the warm task expiring soonest. Return to the preferred task if it later becomes estimated warm again.
- Persist configuration across app restarts, but do **not** persist the preferred task. Auto-select a warm task after restart.

## Estimation and data

- The documented default for GPT-5.6 and later is at least 30 minutes after the most recent cache write **or reuse**. This is a minimum retention policy, not a guarantee of a cache hit. Earlier models depend on their retention mode; GPT-5.5 supports extended retention that is typically around 30 minutes. Source: [OpenAI prompt caching guide](https://developers.openai.com/api/docs/guides/prompt-caching).
- Use a fixed **30-minute estimate for every model**. The user simplified the earlier per-model dictionary proposal on 2026-09-25. There is no unknown-model toast.
- A task displays only one row for its latest model. When the model changes, treat the previous model's estimate as cold and start tracking the new model independently. No model name appears in the taskbar display or popup row.
- Codex does not expose a precise server cache-write/reuse time in supported hooks or the observed local records. Use the **last durable event known to precede the final model request** as a conservative 30-minute anchor: typically the last output of the preceding tool batch, otherwise the prompt-submission event. The remaining time is an estimate and may run out before the server cache actually does.
- Local rollout records contain per-response completion time and token usage, not model-request submission time. They also contain tool output and task identity. Fork history can be copied with new file timestamps, so the parser must isolate the fork's own events. `session_index.jsonl` maps task IDs to actual titles. These are internal formats and can change.
- Cache-hit/miss verification is deferred, although the local records contain cached-token counts that could support later analysis.

## Question and answer record

The question wording below is condensed for readability. The answer and decision are preserved. `Q21` and `Q22` each went through a refinement.

| ID | Question or issue | User answer / decision |
| --- | --- | --- |
| Q1 | What job should the timer do? | Plan thinking time before continuing one or two long sessions while the cache may be hot. |
| Q2 | What display? | Taskbar time with color; click for other sessions; allow a chosen main session. Later clarified exact left-of-`^` gap and overlay. |
| Q3 | Which sessions appear? | All warm sessions, or ones active in a sliding window; default three hours. Final popup choice: all sessions active in last three hours. |
| Q4 | Estimate or confirmed state? | Estimate. Tracking actual cache hits/misses would be useful if available, but is deferred for version 1. |
| Q5 | Does one session mean one Codex task, even if tasks share a project? | Yes. |
| Q6 | How does the main session change? | Manual preference, then auto-switch when it becomes cold. |
| Q7 | Keep recent cold tasks in the popup for three hours? | Yes. |
| Q8 | Five-minute warning toast? | Initially: optional persistent configuration. Later explicitly removed for version 1. |
| Q9 | Send keep-warm requests? | Never. |
| Q10 | Read local usage records for observed cache reuse? | Defer cache-hit tracking. |
| Q11 | Which task replaces a preferred task that goes cold? | Warm task expiring soonest. |
| Q12 | What appears when none are warm? | Minimally sized gray icon only. |
| Q13 | Which task would trigger a five-minute toast? | Tray task only; superseded when Q8 toast was removed. |
| Q14 | How to handle model-specific retention? | Initially: always 30 minutes. Q33 briefly changed this to a dictionary; the latest instruction restores one fixed 30-minute estimate. |
| Q15 | Display while a task is running? | Show Running, then countdown. |
| Q16 | If hooks lack a request timestamp, where does countdown start? | Asked whether a precise timestamp exists. Research found no exposed server timestamp; Q21 refined the conservative anchor. |
| Q17 | How much time can fit in a notification icon? | The user meant a display to the **left** of `^`, not an icon in the notification area to its right. |
| Q18 | Return to the preferred task when it becomes warm again? | Yes. |
| Q19 | Which tasks must version 1 cover? | Local Windows tasks first. |
| Q20 | What happens after switching models? | Previous model's cache is cold for this view; one row per session for latest model. |
| Q21 | Best anchor after inspecting internal rollout records? | Use the last durable event before the final model request, usually the last tool output; otherwise prompt submission. |
| Q22 | Is the exact gap left of `^` required, given Windows does not natively provide a text slot? | Yes; use a taskbar-positioned overlay. |
| Q23 | Should forks share a session? | No. Forks are separate sessions. |
| Q24 | What title should a popup use? | The actual Codex title is required. |
| Q25 | What appears before a manual preference is chosen? | Warm task expiring soonest. |
| Q26 | What does clicking a popup row do? | Pin icon on right selects preference. Opening the Codex task on row click is nice-to-have. Show all recent sessions, including preferred, sorted by anchor/deadline. |
| Q27 | Color thresholds? | Green above 10 minutes; amber 5–10; red below 5; blue running; gray cold. |
| Q28 | Title before Codex names a new task? | Temporarily `Untitled`. |
| Q29 | Persist preferred task and settings? | Persist settings only; auto-select a task each startup. |
| Q30 | Does the overlay sit fully in the marked taskbar gap without covering controls? | The C# app uses a taskbar-owned tool window in the requested position. The user confirmed it stays out of Alt+Tab. Direct painting fixed intermittent dot-only rendering; Windows can still temporarily cover the timer with another topmost surface. |
| Q31 | How should popup countdowns sort? The initial proposal was warm tasks first, soonest deadline first, then recently expired cold tasks. | Warmest at the bottom. This also aligns with Q26's time-based order across recent sessions. |
| Q32 | What time should a cold row show: `Cold · 42m ago` measured from estimated deadline or frozen `00:00`? | The relative cold time sounds good. |
| Q33 | Earlier models may have different retention. Should they still show a fixed 30-minute estimate? | Initially chose a duration dictionary and a one-time unknown-model toast. **Superseded** by the latest instruction: fixed 30 minutes for every model, no dictionary and no toast. |
| Q34 | For Running, show a blue icon and elapsed turn time, with `Running` in the popup, then remaining time when idle? | OK. The later request removed the clock's hover tooltip. |
| Q35 | Primary monitor's bottom taskbar first, with multi-monitor and auto-hide later? | OK. |

## Taskbar implementation findings

- A borderless C# WinForms window can occupy the requested gap and show an icon plus countdown.
- An ordinary topmost window sometimes fell behind Explorer's taskbar during focus changes. Polling `SetWindowPos(HWND_TOPMOST)` brought it back but caused visible flicker. The user observed this directly.
- Making the overlay **owned by the taskbar window** eliminated the observed focus-switching flicker on the user's desktop. Other topmost windows can still cover it temporarily; see [TASKBAR_RESEARCH.md](TASKBAR_RESEARCH.md).
- The app applies `WS_EX_TOOLWINDOW` and removes `WS_EX_APPWINDOW`; Microsoft documents that tool windows are excluded from Alt+Tab. The user confirmed the overlay remains visible and clickable while being absent from Alt+Tab.
