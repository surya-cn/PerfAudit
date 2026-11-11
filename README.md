# PerfAudit v1.9
Performance & Game FPS Auditing Tool

PerfAudit is a Windows utility that logs the CPU usage of a process, with burst detection, and the FPS of a game. Both can run at the same time and are saved into one Excel workbook per run.

## Key Features
- **High-resolution CPU sampling:** the target process is sampled every 100 ms (10 Hz), which captures spikes that fall between conventional one-second polling intervals.
- **Fresh process data:** `.Refresh()` is called on the target process before every sample, so each calculation uses current processor times.
- **Elapsed-time burst detection:** a burst is flagged once CPU usage has stayed above the threshold for 300 ms of continuous time.
- **Passive game FPS logging:** frames are counted from DXGI Present events delivered through Event Tracing for Windows (ETW). PerfAudit does not inject into or hook the game process and only reads system ETW events. Validate this behavior with your own setup.
- **Independent or parallel logging:** the "Select Process (CPU)" and "Select Game (FPS)" dropdowns each have a "None" option. Log CPU only, FPS only, or both on a shared timebase. If one monitored process exits, the other logger keeps running.
- **Type-ahead search in both dropdowns:** typing jumps to and selects the first item whose displayed text starts with what you typed. Backspace removes the last character, Escape clears the search, and the search resets after 1.5 seconds without typing.
- **Excel report:** each run is saved as one `.xlsx` workbook on the Desktop.

## Installation
1. Download `PerfAudit_v1.9.0_Setup.exe` from the Releases page of this repository.
2. Run it. The installer is not code-signed, so Windows SmartScreen may show a warning (choose More info, then Run anyway).
3. The installer checks for .NET Framework 4.8 and installs PerfAudit to Program Files with a Start Menu shortcut. A desktop shortcut is optional.

To uninstall, use Windows Settings, Apps. Reports saved on the Desktop are not removed.

## Requirements
- Windows 10 / 11 (x64)
- .NET Framework 4.8
- Administrator rights: PerfAudit requests them at startup (UAC prompt). FPS logging needs them to create the ETW session.

## How to Use
1. Launch PerfAudit and approve the UAC prompt.
2. Choose a process in "Select Process (CPU)", for example a background service, or "None".
3. Choose a game process in "Select Game (FPS)", for example `game.exe`, or "None". The game must already be running. Use "Refresh List" if it does not appear.
4. If CPU monitoring is selected, set the burst threshold (default 15.0%).
5. Click "Start Monitoring". The button changes to "Stop & Export" and the selectors lock.
6. Click "Stop & Export" when the test is finished. If the only active logger's process exits, the report is exported automatically.

If the game runs in exclusive fullscreen, PerfAudit's window may be hidden. Monitoring continues in the background.

Closing the window during a run asks for confirmation, because the data of a running session is discarded.

## Report Structure
Reports are saved to the Desktop as `PerfAudit_<yyyyMMdd_HHmmss>.xlsx`, with up to four sheets depending on which loggers were used:

- `CPU_Usage`: the 100 ms sample log with millisecond timestamps, CPU usage percentages, burst indicators, and summary header and footer rows.
- `CPU_Graph`: run-wide CPU statistics, a per-second table of peak CPU, and a chart of peak CPU per second with the threshold line and burst markers.
- `FPS_Log`: one row per full second with Timestamp (HH:mm:ss, start of that second), Process, time_s (formatted [mm]:ss), avg_fps and low_1pct_fps. The first and last partial seconds of the run are left out. A full second without frames is logged as 0.
- `FPS_Graph`: Average FPS, Average frametime (ms) and 1% low FPS for the whole run, plus a line chart of avg_fps and 1% low over time that reads directly from `FPS_Log`.

### How the FPS numbers are calculated
- **avg_fps:** the number of frames presented in that second.
- **Average FPS:** total frames divided by the time between the first and the last frame.
- **Average frametime:** the mean time between consecutive frames. Individual frametimes are not written to the workbook.
- **1% low FPS (percentile):** 1000 divided by the 99th-percentile frametime. For the whole run this is the summary value. In `FPS_Log` the same calculation is applied to the frames of each second.

## Build from Source
1. Open `PerfAudit.csproj` in Visual Studio and build the `Release` configuration. NuGet packages are restored automatically.
2. Run the whole `bin\Release` folder, not only `PerfAudit.exe`. It needs the DLLs, `PerfAudit.exe.config` and the `amd64`, `arm64` and `x86` folders.
3. To build the installer, compile `installer\PerfAudit.iss` with Inno Setup after a Release build. The output goes to `installer\Output\`.

## Third-Party Packages and Licensing
- **EPPlus 4.5.3.3:** LGPL. Do not upgrade to version 5 or later, which uses a commercial (Polyform Noncommercial) license.
- **Microsoft.Diagnostics.Tracing.TraceEvent 3.1.8:** MIT. Used to read ETW events.

## Known Limitations
- Frames are counted from DXGI Present events only. Games that do not present through DXGI, such as native Vulkan or OpenGL titles, may not register any frames.
- Only one PerfAudit instance should log FPS at a time. Starting FPS logging stops any ETW session with the same name.
- Seconds around a game closing or loading can contain slow frames, and they are included in the run statistics.
