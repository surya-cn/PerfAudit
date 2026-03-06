PerfAudit v1.7
High-Precision CPU Forensic Auditing Tool

PerfAudit is a specialized C# utility designed for software engineers and QA testers to identify transient performance irregularities—"micro-bursts"—that standard monitoring tools like Windows Task Manager are technically incapable of detecting.

🚀 Key Features
1. High-Resolution 100ms Sampling
While standard tools poll every 1,000ms, PerfAudit samples every 100ms (10Hz). This allows the capture of spikes that occur between standard polling intervals, providing a true representation of software "hitches" and frame-time stutters.

2. Kernel-Level Data Freshness
PerfAudit utilizes the .Refresh() method on the target process before every sample. This bypasses the Windows OS performance counter cache, ensuring the data retrieved is the absolute current processor cycle count directly from the kernel.

3. Smart Burst Detection (3-Sample Logic)
To separate genuine software flaws from background system noise, PerfAudit employs a 3-Sample Streak logic. A [BURST DETECTED] flag is only triggered if the process exceeds the defined threshold for 300ms (3 consecutive samples) or more.

4. Dynamic Decimal Thresholding
Users can input custom CPU thresholds with decimal precision (e.g., 4.1% to monitor a single thread on a 24-thread CPU like the i7-13700HX).

Safety Lock: The threshold input and process selection are automatically locked during active monitoring to ensure audit integrity.

Smart Defaults: Launches with a 15.0% standard threshold for consistent audit baselines.

📊 Audit Export & Traceability
Data is exported to a forensic-grade CSV file on the Desktop.

E1 Metadata Header: The specific threshold used for the test is stored in cell E1 for audit compliance.

Millisecond Timestamps: Every entry includes a high-precision HH:mm:ss.fff timestamp.

Excel-Ready: The 5-column structure is optimized for immediate charting and "Goal-Line" analysis in Excel.
