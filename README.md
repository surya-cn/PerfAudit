SHA-256: 024DACF083EF6DFD9C1B46EBF37D40DC3E65D180E99AD7092AF44CC165B4E8A7 

# PerfAudit
A lightweight, standalone Windows diagnostic tool for high-frequency process monitoring and performance auditing.

## 1. Purpose & Scope
PerfAudit is designed to monitor the CPU performance of specific target processes during live system testing. It provides high-frequency data logging to identify performance spikes, specifically highlighting instances that exceed a 15% CPU usage threshold.

Dynamic Monitoring: Select any running process via a real-time updated dropdown.

High-Frequency Logging: Captures performance data every second.

Automated Export: Data is automatically saved to a CSV file if the target process terminates or the application is closed.

## 2. Security & Elevated Privileges
Administrative Access: This application requires requireAdministrator privileges via the app.manifest. This is strictly necessary to access the Performance Counters of system-level or kernel-level processes that run under elevated user accounts (e.g., SYSTEM).

Binary Integrity: A SHA-256 hash is provided in the release notes to allow users and IT security teams to verify that the .exe binary has not been modified since the build was completed.

TopMost Execution: The application is configured to stay on top of other windows to allow for continuous monitoring during full-screen testing.

## 3. Data Privacy & Handling
Data Collection: The tool captures only four data points: Process Name, PID, Timestamp, and CPU Usage Percentage.

Local Storage: All captured data is stored locally on the user's Desktop in a standard .csv format.

No Network Activity: This tool has zero network communication capabilities. It does not "phone home," upload logs to any cloud service, or interact with external APIs.

Open Transparency: The full source code (Form1.cs, Program.cs, app.manifest) is available for independent security auditing to ensure compliance with corporate security standards.

## 4. Troubleshooting
Missing CPU Data: If CPU usage shows 0% for system processes, ensure the app was launched with "Run as Administrator".

UAC/SmartScreen Warnings: Due to the embedded manifest, Windows may flag the file as "Unknown." Click More Info > Run Anyway to proceed.

CSV Not Appearing: Verify the application has permission to write to the current user's Desktop folder.
