using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using OfficeOpenXml.Drawing;
using OfficeOpenXml.Drawing.Chart;
using OfficeOpenXml.Style;

namespace PerfAudit
{
    public struct CpuSample
    {
        public double TimeS;
        public double CpuUsage;
        public bool IsBurst;
    }

    internal class CpuSecondBucket
    {
        public int Sec { get; set; }
        public double PeakCpu { get; set; }
        public double Threshold { get; set; }
        public double? BurstPeak { get; set; }
        public bool HasBurst { get; set; }
    }

    internal struct FrameRecord
    {
        public string Timestamp;
        public double TimeS;
        public double FrametimeMs;
    }

    internal class SecondBucket
    {
        public int Sec { get; set; }
        public double AvgFps { get; set; }
        public double WorstFtMs { get; set; }
        public string Process { get; set; }
    }

    public static class ExcelReportGenerator
    {
        public static void GenerateReport(
            string outputPath,
            string cpuTempFile,
            List<CpuSample> cpuSamples,
            string cpuProcessName,
            double threshold,
            int totalBurstCount,
            double avgCpu,
            string fpsTempFile,
            string gameProcessName,
            DateTime runDate)
        {
            FileInfo newFile = new FileInfo(outputPath);
            if (newFile.Exists) newFile.Delete();

            using (ExcelPackage package = new ExcelPackage(newFile))
            {
                // Sheet order: CPU_Usage, CPU_Graph, FPS_Log, FPS_Graph (omit the ones not used)

                // Sheet 1: CPU_Usage
                if (!string.IsNullOrEmpty(cpuTempFile) && File.Exists(cpuTempFile))
                {
                    BuildCpuSheet(package, cpuTempFile, threshold, totalBurstCount, avgCpu);
                }

                // Sheet 2: CPU_Graph
                if (!string.IsNullOrEmpty(cpuTempFile) && cpuSamples != null && cpuSamples.Count > 0)
                {
                    BuildCpuGraphSheet(package, cpuSamples, cpuProcessName ?? "CPU Target", threshold, totalBurstCount, avgCpu);
                }

                // Sheet 3: FPS_Log & Sheet 4: FPS_Graph
                if (!string.IsNullOrEmpty(fpsTempFile) && File.Exists(fpsTempFile))
                {
                    BuildFpsSheets(package, fpsTempFile, gameProcessName, runDate);
                }

                package.Save();
            }
        }

        private static void BuildCpuSheet(
            ExcelPackage package,
            string cpuTempFile,
            double threshold,
            int totalBurstCount,
            double avgCpu)
        {
            var ws = package.Workbook.Worksheets.Add("CPU_Usage");

            // Header row replicating CSV layout
            ws.Cells[1, 1].Value = "Timestamp";
            ws.Cells[1, 2].Value = "Process";
            ws.Cells[1, 3].Value = "CPU_Usage_Percent";
            ws.Cells[1, 4].Value = "Notes";
            ws.Cells[1, 5].Value = $"Threshold_Used:{threshold}%";
            ws.Cells[1, 7].Value = $"TOTAL BURST SAMPLES: {totalBurstCount}";
            ws.Cells[1, 9].Value = $"AVG CPU: {Math.Round(avgCpu, 2)}%";
            ws.Row(1).Style.Font.Bold = true;

            int row = 2;
            using (var reader = new StreamReader(cpuTempFile))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(',');
                    if (parts.Length >= 4)
                    {
                        ws.Cells[row, 1].Value = parts[0];
                        ws.Cells[row, 2].Value = parts[1];
                        if (double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double cpuVal))
                            ws.Cells[row, 3].Value = cpuVal;
                        else
                            ws.Cells[row, 3].Value = parts[2];
                        ws.Cells[row, 4].Value = parts[3];
                        row++;
                    }
                }
            }

            // Bottom summary rows
            row++; // Blank row
            ws.Cells[row, 4].Value = "TOTAL BURST SAMPLES:";
            ws.Cells[row, 5].Value = totalBurstCount;
            ws.Row(row).Style.Font.Bold = true;
            row++;

            ws.Cells[row, 4].Value = "AVERAGE SESSION CPU:";
            ws.Cells[row, 5].Value = $"{Math.Round(avgCpu, 2)}%";
            ws.Row(row).Style.Font.Bold = true;

            int sampleCountForAutofit = Math.Min(row, 100);
            ws.Cells[1, 1, sampleCountForAutofit, 9].AutoFitColumns();
        }

        private static void BuildCpuGraphSheet(
            ExcelPackage package,
            List<CpuSample> cpuSamples,
            string cpuProcessName,
            double threshold,
            int totalBurstCount,
            double avgCpu)
        {
            var wsGraph = package.Workbook.Worksheets.Add("CPU_Graph");

            // Row 1: Process title
            wsGraph.Cells[1, 1].Value = $"Process: {cpuProcessName}";
            wsGraph.Cells[1, 1].Style.Font.Bold = true;
            wsGraph.Cells[1, 1].Style.Font.Size = 13;

            int sampleCount = cpuSamples != null ? cpuSamples.Count : 0;
            double durationSeconds = sampleCount > 0 ? (cpuSamples[sampleCount - 1].TimeS - cpuSamples[0].TimeS) : 0.0;
            if (durationSeconds < 0) durationSeconds = 0.0;

            double peakCpu = sampleCount > 0 ? cpuSamples.Max(s => s.CpuUsage) : 0.0;

            // Group by second
            var perSecondMap = new Dictionary<int, List<CpuSample>>();
            if (cpuSamples != null)
            {
                foreach (var s in cpuSamples)
                {
                    int sec = (int)Math.Floor(s.TimeS);
                    if (!perSecondMap.TryGetValue(sec, out var list))
                    {
                        list = new List<CpuSample>();
                        perSecondMap[sec] = list;
                    }
                    list.Add(s);
                }
            }

            int maxSec = sampleCount > 0 ? (int)Math.Floor(cpuSamples[sampleCount - 1].TimeS) : -1;
            var completeSeconds = new List<CpuSecondBucket>();

            foreach (var kv in perSecondMap.OrderBy(k => k.Key))
            {
                if (kv.Key < maxSec && kv.Value.Count > 0)
                {
                    double secPeak = kv.Value.Max(s => s.CpuUsage);
                    bool hasBurst = kv.Value.Any(s => s.IsBurst);

                    completeSeconds.Add(new CpuSecondBucket
                    {
                        Sec = kv.Key,
                        PeakCpu = secPeak,
                        Threshold = threshold,
                        BurstPeak = hasBurst ? (double?)secPeak : null,
                        HasBurst = hasBurst
                    });
                }
            }

            int secondsWithBurst = completeSeconds.Count(cs => cs.HasBurst);

            Color lightFill = Color.FromArgb(242, 244, 247);
            Color headerFill = Color.FromArgb(225, 230, 238);
            Color borderColor = Color.FromArgb(210, 215, 222);

            // 2. SUMMARY BLOCK (rows 3-10, labels in column A, values in column B)
            wsGraph.Cells[3, 1].Value = "Summary Metric";
            wsGraph.Cells[3, 2].Value = "Value";
            wsGraph.Row(3).Style.Font.Bold = true;
            wsGraph.Cells[3, 1, 3, 2].Style.Font.Bold = true;
            wsGraph.Cells[3, 1, 3, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            wsGraph.Cells[3, 1, 3, 2].Style.Fill.BackgroundColor.SetColor(headerFill);

            var summaryRows = new (string Label, object Value, string Format)[]
            {
                ("Average CPU %", Math.Round(avgCpu, 2), "0.00"),
                ("Peak CPU %", Math.Round(peakCpu, 2), "0.00"),
                ("Threshold used %", Math.Round(threshold, 2), "0.00"),
                ("Total burst samples", totalBurstCount, "#,##0"),
                ("Seconds containing a burst", secondsWithBurst, "#,##0"),
                ("Duration ([mm]:ss)", durationSeconds / 86400.0, "[mm]:ss")
            };

            for (int i = 0; i < summaryRows.Length; i++)
            {
                int r = 4 + i;
                wsGraph.Cells[r, 1].Value = summaryRows[i].Label;
                wsGraph.Cells[r, 1].Style.Font.Bold = true;
                wsGraph.Cells[r, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                wsGraph.Cells[r, 1].Style.Fill.BackgroundColor.SetColor(lightFill);

                wsGraph.Cells[r, 2].Value = summaryRows[i].Value;
                if (!string.IsNullOrEmpty(summaryRows[i].Format))
                {
                    wsGraph.Cells[r, 2].Style.Numberformat.Format = summaryRows[i].Format;
                }
                wsGraph.Cells[r, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
                wsGraph.Cells[r, 2].Style.Fill.BackgroundColor.SetColor(lightFill);
            }

            int summaryEndRow = 3 + summaryRows.Length;
            wsGraph.Cells[3, 1, summaryEndRow, 2].Style.Border.Top.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 1, summaryEndRow, 2].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 1, summaryEndRow, 2].Style.Border.Left.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 1, summaryEndRow, 2].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 1, summaryEndRow, 2].Style.Border.Top.Color.SetColor(borderColor);
            wsGraph.Cells[3, 1, summaryEndRow, 2].Style.Border.Bottom.Color.SetColor(borderColor);
            wsGraph.Cells[3, 1, summaryEndRow, 2].Style.Border.Left.Color.SetColor(borderColor);
            wsGraph.Cells[3, 1, summaryEndRow, 2].Style.Border.Right.Color.SetColor(borderColor);

            // 3. PER-SECOND TABLE (start at row 15, columns A-D)
            wsGraph.Cells[15, 1].Value = "time_s";
            wsGraph.Cells[15, 2].Value = "peak_cpu_pct";
            wsGraph.Cells[15, 3].Value = "threshold_pct";
            wsGraph.Cells[15, 4].Value = "burst_peak_pct";
            wsGraph.Row(15).Style.Font.Bold = true;

            int tableRow = 16;
            foreach (var cs in completeSeconds)
            {
                wsGraph.Cells[tableRow, 1].Value = (double)cs.Sec / 86400.0;
                wsGraph.Cells[tableRow, 1].Style.Numberformat.Format = "[mm]:ss";

                wsGraph.Cells[tableRow, 2].Value = Math.Round(cs.PeakCpu, 2);
                wsGraph.Cells[tableRow, 2].Style.Numberformat.Format = "0.00";

                wsGraph.Cells[tableRow, 3].Value = Math.Round(cs.Threshold, 2);
                wsGraph.Cells[tableRow, 3].Style.Numberformat.Format = "0.00";

                if (cs.BurstPeak.HasValue)
                {
                    wsGraph.Cells[tableRow, 4].Value = Math.Round(cs.BurstPeak.Value, 2);
                    wsGraph.Cells[tableRow, 4].Style.Numberformat.Format = "0.00";
                }

                tableRow++;
            }

            int tableEndRow = tableRow - 1;
            wsGraph.Cells[1, 1, Math.Max(tableEndRow, 16), 4].AutoFitColumns();

            // 4. CHART "<process> - CPU % vs time" (placed at Col F, 900x350 px)
            if (completeSeconds.Count > 1)
            {
                var chart = wsGraph.Drawings.AddChart("CpuChart", eChartType.Line) as ExcelLineChart;
                chart.Title.Text = $"{cpuProcessName} - CPU % vs time";
                chart.SetPosition(1, 0, 5, 0); // Row 2, Col F
                chart.SetSize(900, 350);

                // Series 1: peak_cpu_pct, thin line (about 1.5 pt), no markers, strong blue.
                var ser1 = chart.Series.Add(wsGraph.Cells[16, 2, tableEndRow, 2], wsGraph.Cells[16, 1, tableEndRow, 1]) as ExcelLineChartSerie;
                ser1.Header = "Peak CPU % (per second)";
                ser1.LineWidth = 1.5;
                ser1.LineColor = Color.FromArgb(0, 114, 206); // strong blue
                ser1.Marker = eMarkerStyle.None;

                // Series 2: threshold_pct, thin dashed grey line, no markers.
                var ser2 = chart.Series.Add(wsGraph.Cells[16, 3, tableEndRow, 3], wsGraph.Cells[16, 1, tableEndRow, 1]) as ExcelLineChartSerie;
                ser2.Header = "Threshold";
                ser2.LineWidth = 1.0;
                ser2.LineColor = Color.FromArgb(140, 140, 140); // grey
                ser2.Border.LineStyle = eLineStyle.Dash;
                ser2.Marker = eMarkerStyle.None;

                // Series 3: burst_peak_pct, NO connecting line, red circle markers (size about 6) only. Empty cells must show as gaps, not as zero.
                var ser3 = chart.Series.Add(wsGraph.Cells[16, 4, tableEndRow, 4], wsGraph.Cells[16, 1, tableEndRow, 1]) as ExcelLineChartSerie;
                ser3.Header = "Burst";
                ser3.Marker = eMarkerStyle.Circle;
                ser3.MarkerSize = 6;
                ser3.MarkerLineColor = Color.Red;
                ser3.Border.Fill.Style = eFillStyle.NoFill;

                chart.DisplayBlanksAs = eDisplayBlanksAs.Gap;

                chart.YAxis.MinValue = 0.0;
                double maxVal = Math.Min(100.0, Math.Max(threshold + 10.0, Math.Ceiling(peakCpu * 1.1 / 10.0) * 10.0));
                if (maxVal < 20.0) maxVal = 20.0;
                chart.YAxis.MaxValue = maxVal;

                chart.YAxis.MajorGridlines.Fill.Style = eFillStyle.SolidFill;
                chart.YAxis.MajorGridlines.Fill.Color = Color.FromArgb(220, 220, 220); // #DCDCDC

                chart.XAxis.Border.Fill.Style = eFillStyle.SolidFill;
                chart.XAxis.Border.Fill.Color = Color.FromArgb(215, 215, 215);
                chart.YAxis.Border.Fill.Style = eFillStyle.SolidFill;
                chart.YAxis.Border.Fill.Color = Color.FromArgb(215, 215, 215);

                chart.XAxis.MajorUnit = 30.0;
                chart.XAxis.Format = "[mm]:ss";
                chart.Legend.Position = eLegendPosition.Bottom;
            }
        }

        private static void BuildFpsSheets(
            ExcelPackage package,
            string fpsTempFile,
            string gameProcessName,
            DateTime runDate)
        {
            int sheetNum = 1;
            string sheetName = "FPS_Log";
            ExcelWorksheet wsLog = package.Workbook.Worksheets.Add(sheetName);
            SetupFpsLogHeader(wsLog);

            int row = 2;
            var allFrames = new List<FrameRecord>();

            using (var reader = new StreamReader(fpsTempFile))
            {
                string header = reader.ReadLine(); // Skip header
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(',');
                    if (parts.Length >= 5)
                    {
                        if (row > 1000000)
                        {
                            wsLog.Cells[1, 1, 100, 5].AutoFitColumns();
                            sheetNum++;
                            wsLog = package.Workbook.Worksheets.Add($"FPS_Log_{sheetNum}");
                            SetupFpsLogHeader(wsLog);
                            row = 2;
                        }

                        wsLog.Cells[row, 1].Value = parts[0];
                        wsLog.Cells[row, 2].Value = parts[1];

                        if (double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double timeS))
                        {
                            wsLog.Cells[row, 3].Value = timeS;
                            wsLog.Cells[row, 3].Style.Numberformat.Format = "0.000";

                            double ftMs = 0.0;
                            if (double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out ftMs))
                            {
                                wsLog.Cells[row, 4].Value = ftMs;
                                wsLog.Cells[row, 4].Style.Numberformat.Format = "0.000";
                            }
                            else
                            {
                                wsLog.Cells[row, 4].Value = parts[3];
                            }

                            allFrames.Add(new FrameRecord
                            {
                                Timestamp = parts[0],
                                TimeS = timeS,
                                FrametimeMs = ftMs
                            });
                        }
                        else
                        {
                            wsLog.Cells[row, 3].Value = parts[2];
                            wsLog.Cells[row, 4].Value = parts[3];
                        }

                        if (int.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out int fps1s))
                            wsLog.Cells[row, 5].Value = fps1s;
                        else
                            wsLog.Cells[row, 5].Value = parts[4];

                        row++;
                    }
                }
            }

            wsLog.Cells[1, 1, Math.Min(row, 100), 5].AutoFitColumns();

            // Sheet 3: FPS_Graph
            BuildFpsGraphSheet(package, allFrames, gameProcessName, runDate);
        }

        private static void BuildFpsGraphSheet(
            ExcelPackage package,
            List<FrameRecord> frames,
            string gameProcessName,
            DateTime runDate)
        {
            var wsGraph = package.Workbook.Worksheets.Add("FPS_Graph");

            // Row 1: Game name
            wsGraph.Cells[1, 1].Value = $"Game: {gameProcessName}";
            wsGraph.Cells[1, 1].Style.Font.Bold = true;
            wsGraph.Cells[1, 1].Style.Font.Size = 13;

            int totalFrames = frames.Count;
            // Exclude the very first frame (frametime 0)
            var validFrames = frames.Skip(1).Where(f => f.FrametimeMs > 0).ToList();

            double durationSeconds = totalFrames > 0 ? (frames[frames.Count - 1].TimeS - frames[0].TimeS) : 0.0;
            if (durationSeconds < 0) durationSeconds = 0.0;
            TimeSpan durationSpan = TimeSpan.FromSeconds(durationSeconds);
            string durationStr = $"{(int)durationSpan.TotalMinutes:D2}:{durationSpan.Seconds:D2}";

            double avgFps = (durationSeconds > 0 && totalFrames > 0) ? (totalFrames / durationSeconds) : 0.0;

            var sortedFt = validFrames.Select(f => f.FrametimeMs).OrderBy(ft => ft).ToList();
            double fps1PctLow = 0.0;
            double fps01PctLow = 0.0;
            double worstFrameMs = 0.0;

            if (sortedFt.Count > 0)
            {
                int idx99 = Math.Min(sortedFt.Count - 1, Math.Max(0, (int)Math.Floor(0.99 * (sortedFt.Count - 1))));
                double p99Ft = sortedFt[idx99];
                fps1PctLow = p99Ft > 0 ? 1000.0 / p99Ft : avgFps;

                int idx999 = Math.Min(sortedFt.Count - 1, Math.Max(0, (int)Math.Floor(0.999 * (sortedFt.Count - 1))));
                double p999Ft = sortedFt[idx999];
                fps01PctLow = p999Ft > 0 ? 1000.0 / p999Ft : avgFps;

                worstFrameMs = sortedFt[sortedFt.Count - 1];
            }

            int framesOver50 = validFrames.Count(f => f.FrametimeMs > 50.0);
            int framesOver100 = validFrames.Count(f => f.FrametimeMs > 100.0);

            // Group per second
            var perSecondBuckets = new Dictionary<int, List<double>>();
            foreach (var f in frames)
            {
                int sec = (int)Math.Floor(f.TimeS);
                if (!perSecondBuckets.TryGetValue(sec, out var list))
                {
                    list = new List<double>();
                    perSecondBuckets[sec] = list;
                }
                if (f.FrametimeMs > 0) list.Add(f.FrametimeMs);
            }

            int maxSec = frames.Count > 0 ? (int)Math.Floor(frames[frames.Count - 1].TimeS) : -1;
            var completeSeconds = new List<SecondBucket>();
            foreach (var kv in perSecondBuckets.OrderBy(k => k.Key))
            {
                // Exclude the final incomplete second
                if (kv.Key < maxSec && kv.Value.Count > 0)
                {
                    completeSeconds.Add(new SecondBucket
                    {
                        Sec = kv.Key,
                        AvgFps = kv.Value.Count,
                        WorstFtMs = kv.Value.Max(),
                        Process = gameProcessName
                    });
                }
            }

            double lowest1sFps = completeSeconds.Count > 0 ? completeSeconds.Min(cs => cs.AvgFps) : 0.0;

            Color lightFill = Color.FromArgb(242, 244, 247);
            Color headerFill = Color.FromArgb(225, 230, 238);
            Color borderColor = Color.FromArgb(210, 215, 222);

            // 1. SUMMARY BLOCK (rows 3-12, labels in column A, values in B)
            wsGraph.Cells[3, 1].Value = "Summary Metric";
            wsGraph.Cells[3, 2].Value = "Value";
            wsGraph.Row(3).Style.Font.Bold = true;
            wsGraph.Cells[3, 1, 3, 2].Style.Font.Bold = true;
            wsGraph.Cells[3, 1, 3, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            wsGraph.Cells[3, 1, 3, 2].Style.Fill.BackgroundColor.SetColor(headerFill);

            var summaryRows = new (string Label, object Value, string Format)[]
            {
                ("Average FPS", Math.Round(avgFps, 1), "0.0"),
                ("1% low FPS (percentile)", Math.Round(fps1PctLow, 1), "0.0"),
                ("0.1% low FPS (percentile)", Math.Round(fps01PctLow, 1), "0.0"),
                ("Lowest 1-second FPS", Math.Round(lowest1sFps, 1), "0.0"),
                ("Worst frame (ms)", Math.Round(worstFrameMs, 1), "0.0"),
                ("Frames over 50 ms", framesOver50, "#,##0"),
                ("Frames over 100 ms", framesOver100, "#,##0"),
                ("Total frames", totalFrames, "#,##0"),
                ("Duration ([mm]:ss)", durationSeconds / 86400.0, "[mm]:ss")
            };

            for (int i = 0; i < summaryRows.Length; i++)
            {
                int r = 4 + i;
                wsGraph.Cells[r, 1].Value = summaryRows[i].Label;
                wsGraph.Cells[r, 1].Style.Font.Bold = true;
                wsGraph.Cells[r, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                wsGraph.Cells[r, 1].Style.Fill.BackgroundColor.SetColor(lightFill);

                wsGraph.Cells[r, 2].Value = summaryRows[i].Value;
                if (!string.IsNullOrEmpty(summaryRows[i].Format))
                {
                    wsGraph.Cells[r, 2].Style.Numberformat.Format = summaryRows[i].Format;
                }
                wsGraph.Cells[r, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
                wsGraph.Cells[r, 2].Style.Fill.BackgroundColor.SetColor(lightFill);
            }

            wsGraph.Cells[3, 1, 12, 2].Style.Border.Top.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 1, 12, 2].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 1, 12, 2].Style.Border.Left.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 1, 12, 2].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 1, 12, 2].Style.Border.Top.Color.SetColor(borderColor);
            wsGraph.Cells[3, 1, 12, 2].Style.Border.Bottom.Color.SetColor(borderColor);
            wsGraph.Cells[3, 1, 12, 2].Style.Border.Left.Color.SetColor(borderColor);
            wsGraph.Cells[3, 1, 12, 2].Style.Border.Right.Color.SetColor(borderColor);

            // 3. WORST 10 FRAMES table (Cols D, E, F; rows 3-13)
            var worst10 = validFrames.OrderByDescending(f => f.FrametimeMs).Take(10).ToList();

            wsGraph.Cells[3, 4].Value = "Timestamp";
            wsGraph.Cells[3, 5].Value = "time_s";
            wsGraph.Cells[3, 6].Value = "frametime_ms";
            wsGraph.Cells[3, 4, 3, 6].Style.Font.Bold = true;
            wsGraph.Cells[3, 4, 3, 6].Style.Fill.PatternType = ExcelFillStyle.Solid;
            wsGraph.Cells[3, 4, 3, 6].Style.Fill.BackgroundColor.SetColor(headerFill);

            for (int i = 0; i < worst10.Count; i++)
            {
                int r = 4 + i;
                wsGraph.Cells[r, 4].Value = worst10[i].Timestamp.StartsWith("'") ? worst10[i].Timestamp : $"' {worst10[i].Timestamp}";
                wsGraph.Cells[r, 5].Value = worst10[i].TimeS;
                wsGraph.Cells[r, 5].Style.Numberformat.Format = "0.000";
                wsGraph.Cells[r, 6].Value = worst10[i].FrametimeMs;
                wsGraph.Cells[r, 6].Style.Numberformat.Format = "0.000";

                wsGraph.Cells[r, 4, r, 6].Style.Fill.PatternType = ExcelFillStyle.Solid;
                wsGraph.Cells[r, 4, r, 6].Style.Fill.BackgroundColor.SetColor(lightFill);
            }

            int worst10EndRow = 3 + Math.Max(1, worst10.Count);
            wsGraph.Cells[3, 4, worst10EndRow, 6].Style.Border.Top.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 4, worst10EndRow, 6].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 4, worst10EndRow, 6].Style.Border.Left.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 4, worst10EndRow, 6].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 4, worst10EndRow, 6].Style.Border.Top.Color.SetColor(borderColor);
            wsGraph.Cells[3, 4, worst10EndRow, 6].Style.Border.Bottom.Color.SetColor(borderColor);
            wsGraph.Cells[3, 4, worst10EndRow, 6].Style.Border.Left.Color.SetColor(borderColor);
            wsGraph.Cells[3, 4, worst10EndRow, 6].Style.Border.Right.Color.SetColor(borderColor);

            // 2. PER-SECOND TABLE (start at row 15)
            wsGraph.Cells[15, 1].Value = "time_s";
            wsGraph.Cells[15, 2].Value = "avg_fps";
            wsGraph.Cells[15, 3].Value = "worst_frametime_ms";
            wsGraph.Cells[15, 4].Value = "Process";
            wsGraph.Row(15).Style.Font.Bold = true;

            int tableRow = 16;
            foreach (var cs in completeSeconds)
            {
                wsGraph.Cells[tableRow, 1].Value = (double)cs.Sec / 86400.0;
                wsGraph.Cells[tableRow, 1].Style.Numberformat.Format = "[mm]:ss";

                wsGraph.Cells[tableRow, 2].Value = Math.Round(cs.AvgFps, 1);
                wsGraph.Cells[tableRow, 2].Style.Numberformat.Format = "0.0";

                wsGraph.Cells[tableRow, 3].Value = Math.Round(cs.WorstFtMs, 1);
                wsGraph.Cells[tableRow, 3].Style.Numberformat.Format = "0.0";

                wsGraph.Cells[tableRow, 4].Value = cs.Process;
                tableRow++;
            }

            int tableEndRow = tableRow - 1;
            wsGraph.Cells[1, 1, Math.Max(tableEndRow, 16), 6].AutoFitColumns();

            // 4. CHARTS (Stacked at Col I, row 2 and row 21, 900x350 px each)
            if (completeSeconds.Count > 1)
            {
                // CHART 1: <game> - FPS
                var chart1 = wsGraph.Drawings.AddChart("FPSChart", eChartType.Line) as ExcelLineChart;
                chart1.Title.Text = $"{gameProcessName} - FPS";
                chart1.SetPosition(1, 0, 8, 0); // Row 2, Col I
                chart1.SetSize(900, 350);

                var ser1 = chart1.Series.Add(wsGraph.Cells[16, 2, tableEndRow, 2], wsGraph.Cells[16, 1, tableEndRow, 1]) as ExcelLineChartSerie;
                ser1.Header = "Average FPS";
                ser1.LineWidth = 1.5;
                ser1.LineColor = Color.FromArgb(0, 150, 220); // strong teal/blue
                ser1.Marker = eMarkerStyle.None;

                chart1.YAxis.MinValue = 0.0;
                chart1.YAxis.MajorGridlines.Fill.Style = eFillStyle.SolidFill;
                chart1.YAxis.MajorGridlines.Fill.Color = Color.FromArgb(220, 220, 220);

                chart1.XAxis.Border.Fill.Style = eFillStyle.SolidFill;
                chart1.XAxis.Border.Fill.Color = Color.FromArgb(215, 215, 215);
                chart1.YAxis.Border.Fill.Style = eFillStyle.SolidFill;
                chart1.YAxis.Border.Fill.Color = Color.FromArgb(215, 215, 215);

                chart1.XAxis.MajorUnit = 30.0;
                chart1.XAxis.Format = "[mm]:ss";
                chart1.Legend.Position = eLegendPosition.Bottom;

                // CHART 2: <game> - Worst frametime per second (ms) [Spikes are Stutters]
                var chart2 = wsGraph.Drawings.AddChart("FrametimeChart", eChartType.Line) as ExcelLineChart;
                chart2.Title.Text = $"{gameProcessName} - Worst frametime per second (ms) [Spikes are Stutters]";
                chart2.SetPosition(20, 0, 8, 0); // Row 21, Col I
                chart2.SetSize(900, 350);

                var ser2 = chart2.Series.Add(wsGraph.Cells[16, 3, tableEndRow, 3], wsGraph.Cells[16, 1, tableEndRow, 1]) as ExcelLineChartSerie;
                ser2.Header = "Worst Frametime (ms)";
                ser2.LineWidth = 1.5;
                ser2.LineColor = Color.FromArgb(220, 60, 50); // strong red/crimson
                ser2.Marker = eMarkerStyle.None;

                chart2.YAxis.MinValue = 0.0;
                chart2.YAxis.MajorGridlines.Fill.Style = eFillStyle.SolidFill;
                chart2.YAxis.MajorGridlines.Fill.Color = Color.FromArgb(220, 220, 220);

                chart2.XAxis.Border.Fill.Style = eFillStyle.SolidFill;
                chart2.XAxis.Border.Fill.Color = Color.FromArgb(215, 215, 215);
                chart2.YAxis.Border.Fill.Style = eFillStyle.SolidFill;
                chart2.YAxis.Border.Fill.Color = Color.FromArgb(215, 215, 215);

                chart2.XAxis.MajorUnit = 30.0;
                chart2.XAxis.Format = "[mm]:ss";
                chart2.Legend.Position = eLegendPosition.Bottom;
            }
        }

        private static void SetupFpsLogHeader(ExcelWorksheet ws)
        {
            ws.Cells[1, 1].Value = "Timestamp";
            ws.Cells[1, 2].Value = "Process";
            ws.Cells[1, 3].Value = "time_s";
            ws.Cells[1, 4].Value = "frametime_ms";
            ws.Cells[1, 5].Value = "fps_1s";
            ws.Row(1).Style.Font.Bold = true;
            ws.View.FreezePanes(2, 1);
        }
    }
}
