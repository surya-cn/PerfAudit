PerfAudit v1.9
High-Precision Performance & Game FPS Auditing Tool

PerfAudit is a specialized C# utility designed for software engineers and QA testers to identify transient performance irregularities—"micro-bursts"—and frame pacing stutters that standard monitoring tools like Windows Task Manager are technically incapable of detecting. PerfAudit combines kernel-fresh CPU sampling with passive game FPS logging.

🚀 Key Features
1. High-Resolution 100ms CPU Sampling
While standard tools poll every 1,000ms, PerfAudit samples target processes every 100ms (10Hz). This captures transient spikes occurring between conventional polling intervals, exposing software hitches.

2. Kernel-Level Data Freshness
PerfAudit calls `.Refresh()` on the target process before every sample, bypassing cached Windows performance counters to ensure processor cycle calculations reflect immediate kernel state.

3. Elapsed-Time Burst Detection
To distinguish genuine software stalls from background noise, PerfAudit accumulates continuous over-threshold execution time. A burst is flagged only once 300ms of sustained over-threshold activity has elapsed.

4. Passive Game FPS & Frametime Logging
PerfAudit captures frame presentations using kernel Event Tracing for Windows (ETW) subscribed to authoritative DXGI Present events. The tool does not inject into or hook the game process and only reads system ETW events; validate behavior with your own setup.

5. Dual Independent & Parallel Monitoring
Equipped with independent dropdown selectors for CPU target ("Select Process (CPU)") and game target ("Select Game (FPS)"), each with a "None" option. Monitor CPU only, game FPS only, or both concurrently on a synchronized high-resolution timebase. If one target exits prematurely, the remaining logger continues uninterrupted.

6. Incremental Type-Ahead Search
Both dropdowns feature incremental prefix search. Typing characters jumps to and selects the first matching item in list order by displayed text without resetting between keystrokes. Includes full Backspace and Escape controls and an automatic 1.5-second inactivity reset.

7. Multi-Worksheet Excel (.xlsx) Export
Session data is automatically packaged into a single Excel workbook (`.xlsx`) on the Desktop, featuring dedicated data and analytical graph worksheets.

📋 Requirements
- Operating System: Windows 10 / 11 (x64)
- Runtime: .NET Framework 4.8
- Privileges: Administrator rights (the user must launch PerfAudit as administrator for ETW session creation during FPS logging; app.manifest is unchanged)

🎮 How to Use
1. Launch PerfAudit as Administrator.
2. Select a target process from "Select Process (CPU)" (e.g., a background service) or choose "None".
3. Select a target game from "Select Game (FPS)" (e.g., `game.exe`) or choose "None".
4. Adjust the CPU threshold percentage if CPU monitoring is enabled (defaults to 15.0%).
5. Click "Start Monitoring" (button toggles to "Stop & Export", and process inputs lock).
6. Click "Stop & Export" when testing completes (or allow automatic stop if a single monitored process exits).

📊 Report Structure
Reports are saved directly to the Desktop as `PerfAudit_<yyyyMMdd_HHmmss>.xlsx` with up to four sheets depending on active loggers:
- `CPU_Usage`: Raw 100ms sample log with millisecond timestamps, CPU usage percentages, burst indicators, and static summary headers/footers.
- `CPU_Graph`: Run-wide CPU statistics, per-second peak CPU vs. time table, and an embedded chart plotting peak CPU %, threshold line, and discrete burst markers.
- `FPS_Log`: Frame-by-frame log tracking timestamps, game process name, elapsed seconds, frame duration (ms), and rolling 1-second FPS.
- `FPS_Graph`: Comprehensive FPS metrics (Average FPS, 1% low FPS (percentile), 0.1% low FPS (percentile), lowest 1s FPS, worst frame duration, stutter counts >50ms and >100ms), worst 10 frames table, per-second table formatted in `[mm]:ss`, and embedded charts for FPS trends and frametime spikes.

🛠️ Build Notes
- Target Framework: .NET Framework 4.8
- Build Configuration: Visual Studio 2022+ / MSBuild (`Configuration=Release`)

📦 Third-Party Packages & Licensing
- EPPlus (Version 4.5.3.3) — Licensed under LGPL-2.1. Note: EPPlus must not be upgraded to 5.0 or later, as versions 5+ switched to a commercial license (Polyform Noncommercial).
- Microsoft.Diagnostics.Tracing.TraceEvent (Version 3.1.8) — Licensed under MIT. Used for kernel ETW event subscription.

⚠️ Known Limitations
- Frames are captured exclusively via DXGI Present events. Titles presenting exclusively through native Vulkan or OpenGL without a DXGI swapchain layer will not register frame presentations.
