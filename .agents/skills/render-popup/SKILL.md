---
name: render-popup
description: Render the Cache Timer popup to PNGs and inspect them when changing its appearance, diagnosing layout issues, or checking visual fixes.
---

# Render and inspect the popup

Use the real WinForms controls to iterate on popup appearance on Windows with the .NET 10 SDK. Run commands from the repository root.

## Visual iteration

1. Capture a baseline when comparing an existing layout:

   ```powershell
   dotnet run --project .\tools\CacheTimer.Render\CacheTimer.Render.csproj -- --output .\artifacts\popup-before
   ```

2. Edit `src/CacheTimer/SessionsPopupForm.cs` for the requested change, then render the current source:

   ```powershell
   dotnet run --project .\tools\CacheTimer.Render\CacheTimer.Render.csproj
   ```

3. Open the generated PNGs with the available image-viewing tool, using absolute paths. Inspect the affected examples and compare with the baseline. Check text alignment, ellipsis, time-label fit, dot placement, pin highlight, footer spacing, and scrolling as relevant. A successful render alone does not verify appearance.

4. Repeat the edit, render, and image inspection until the requested visual issue is resolved. Inspect all five fixtures after the final change to check for layout regressions, then run:

   ```powershell
   dotnet run --project .\tests\CacheTimer.Regression\CacheTimer.Regression.csproj
   ```

Report the visual result and regression outcome, and show a relevant final image when useful.

## Captures

The default output directory is `artifacts/popup/`, ignored by Git. Fixtures use synthetic task titles and a fixed clock:

| Image | Coverage |
| --- | --- |
| `mixed.png` | All status colors, short and ellipsized titles, preferred task |
| `empty.png` | Empty-list message and footer |
| `long-text.png` | Long titles and long cold/running durations |
| `scroll-top.png` | Overflowing list at the top |
| `scroll-bottom.png` | Overflowing list at the bottom |

The renderer briefly shows the popup offscreen to calculate WinForms layout and native scrollbars, then uses `DrawToBitmap`. It renders at the current Windows display DPI and fonts; keep machine and scaling settings consistent for before/after comparisons. Desktop positioning, focus, occlusion, and hover behavior require live checks.

For an issue involving actual recent tasks, add `--live`. This writes `live.png` with real task titles from the default three-hour window, without loading or changing saved settings or pin choice. Use fixtures for shareable captures.
