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
            List<FpsFrame> fpsFrames,
            string gameProcessName)
        {
            FileInfo newFile = new FileInfo(outputPath);
            if (newFile.Exists) newFile.Delete();

            using (ExcelPackage package = new ExcelPackage(newFile))
            {
                if (!string.IsNullOrEmpty(cpuTempFile) && File.Exists(cpuTempFile))
                {
                    BuildCpuSheet(package, cpuTempFile, threshold, totalBurstCount, avgCpu);
                }

                if (!string.IsNullOrEmpty(cpuTempFile) && cpuSamples != null && cpuSamples.Count > 0)
                {
                    BuildCpuGraphSheet(package, cpuSamples, cpuProcessName ?? "CPU Target", threshold, totalBurstCount, avgCpu);
                }

                if (fpsFrames != null)
                {
                    BuildFpsSheets(package, fpsFrames, gameProcessName ?? "Game");
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

            row++;

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

            wsGraph.Cells[1, 1].Value = $"Process: {cpuProcessName}";
            wsGraph.Cells[1, 1].Style.Font.Bold = true;
            wsGraph.Cells[1, 1].Style.Font.Size = 13;

            int sampleCount = cpuSamples != null ? cpuSamples.Count : 0;
            double durationSeconds = sampleCount > 0 ? (cpuSamples[sampleCount - 1].TimeS - cpuSamples[0].TimeS) : 0.0;
            if (durationSeconds < 0) durationSeconds = 0.0;

            double peakCpu = sampleCount > 0 ? cpuSamples.Max(s => s.CpuUsage) : 0.0;

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

            if (completeSeconds.Count > 1)
            {
                var chart = wsGraph.Drawings.AddChart("CpuChart", eChartType.Line) as ExcelLineChart;
                chart.Title.Text = $"{cpuProcessName} - CPU % vs time";
                chart.SetPosition(1, 0, 5, 0);

                chart.SetSize(900, 350);

                var ser1 = chart.Series.Add(wsGraph.Cells[16, 2, tableEndRow, 2], wsGraph.Cells[16, 1, tableEndRow, 1]) as ExcelLineChartSerie;
                ser1.Header = "Peak CPU % (per second)";
                ser1.LineWidth = 1.5;
                ser1.LineColor = Color.FromArgb(0, 114, 206);

                ser1.Marker = eMarkerStyle.None;

                var ser2 = chart.Series.Add(wsGraph.Cells[16, 3, tableEndRow, 3], wsGraph.Cells[16, 1, tableEndRow, 1]) as ExcelLineChartSerie;
                ser2.Header = "Threshold";
                ser2.LineWidth = 1.0;
                ser2.LineColor = Color.FromArgb(140, 140, 140);

                ser2.Border.LineStyle = eLineStyle.Dash;
                ser2.Marker = eMarkerStyle.None;

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
                chart.YAxis.MajorGridlines.Fill.Color = Color.FromArgb(220, 220, 220);


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
            List<FpsFrame> frames,
            string gameProcessName)
        {
            ExcelWorksheet wsLog = package.Workbook.Worksheets.Add("FPS_Log");
            wsLog.Cells[1, 1].Value = "Timestamp";
            wsLog.Cells[1, 2].Value = "Process";
            wsLog.Cells[1, 3].Value = "time_s";
            wsLog.Cells[1, 4].Value = "avg_fps";
            wsLog.Cells[1, 5].Value = "low_1pct_fps";
            wsLog.Row(1).Style.Font.Bold = true;
            wsLog.View.FreezePanes(2, 1);

            int row = 2;
            bool hasFullSeconds = false;

            if (frames != null && frames.Count > 0)
            {
                int firstSec = (int)Math.Floor(frames[0].TimeS);
                int lastSec = (int)Math.Floor(frames[frames.Count - 1].TimeS);
                if (firstSec + 1 <= lastSec - 1)
                {
                    hasFullSeconds = true;
                    DateTime sessionStart = frames[0].WallClock.AddSeconds(-frames[0].TimeS);

                    var perSecondMap = new Dictionary<int, List<FpsFrame>>();
                    foreach (var f in frames)
                    {
                        int s = (int)Math.Floor(f.TimeS);
                        if (!perSecondMap.TryGetValue(s, out var list))
                        {
                            list = new List<FpsFrame>();
                            perSecondMap[s] = list;
                        }
                        list.Add(f);
                    }

                    for (int sec = firstSec + 1; sec <= lastSec - 1; sec++)
                    {
                        string timestamp = sessionStart.AddSeconds(sec).ToString("HH:mm:ss");
                        wsLog.Cells[row, 1].Value = timestamp;
                        wsLog.Cells[row, 2].Value = gameProcessName;
                        wsLog.Cells[row, 3].Value = (double)sec / 86400.0;
                        wsLog.Cells[row, 3].Style.Numberformat.Format = "[mm]:ss";

                        if (perSecondMap.TryGetValue(sec, out var secFrames) && secFrames.Count > 0)
                        {
                            int count = secFrames.Count;
                            wsLog.Cells[row, 4].Value = (double)count;
                            wsLog.Cells[row, 4].Style.Numberformat.Format = "0.0";

                            var secValidFt = secFrames.Select(f => f.FrametimeMs).Where(ft => ft > 0).OrderBy(ft => ft).ToList();
                            if (secValidFt.Count > 0)
                            {
                                int idx = Math.Min(secValidFt.Count - 1, Math.Max(0, (int)Math.Floor(0.99 * (secValidFt.Count - 1))));
                                double p99 = secValidFt[idx];
                                double low1Pct = p99 > 0 ? 1000.0 / p99 : 0.0;
                                wsLog.Cells[row, 5].Value = Math.Round(low1Pct, 1);
                            }
                            else
                            {
                                wsLog.Cells[row, 5].Value = 0.0;
                            }
                            wsLog.Cells[row, 5].Style.Numberformat.Format = "0.0";
                        }
                        else
                        {
                            wsLog.Cells[row, 4].Value = 0.0;
                            wsLog.Cells[row, 4].Style.Numberformat.Format = "0.0";
                            wsLog.Cells[row, 5].Value = 0.0;
                            wsLog.Cells[row, 5].Style.Numberformat.Format = "0.0";
                        }

                        row++;
                    }
                }
            }

            int endRow = row - 1;
            wsLog.Cells[1, 1, Math.Max(endRow, 1), 5].AutoFitColumns();

            BuildFpsGraphSheet(package, wsLog, frames, gameProcessName, hasFullSeconds, endRow);
        }

        private static void BuildFpsGraphSheet(
            ExcelPackage package,
            ExcelWorksheet wsLog,
            List<FpsFrame> frames,
            string gameProcessName,
            bool hasFullSeconds,
            int endRow)
        {
            var wsGraph = package.Workbook.Worksheets.Add("FPS_Graph");

            wsGraph.Cells[1, 1].Value = $"Game: {gameProcessName}";
            wsGraph.Cells[1, 1].Style.Font.Bold = true;
            wsGraph.Cells[1, 1].Style.Font.Size = 13;

            int totalFrames = frames != null ? frames.Count : 0;
            var validFrames = frames != null ? frames.Skip(1).Where(f => f.FrametimeMs > 0).ToList() : new List<FpsFrame>();

            double durationSeconds = totalFrames > 0 ? (frames[frames.Count - 1].TimeS - frames[0].TimeS) : 0.0;
            if (durationSeconds < 0) durationSeconds = 0.0;

            double avgFps = (durationSeconds > 0 && totalFrames > 0) ? (totalFrames / durationSeconds) : 0.0;
            double avgFrametimeMs = validFrames.Count > 0 ? validFrames.Average(f => f.FrametimeMs) : 0.0;

            var sortedFt = validFrames.Select(f => f.FrametimeMs).OrderBy(ft => ft).ToList();
            double fps1PctLow = 0.0;

            if (sortedFt.Count > 0)
            {
                int idx99 = Math.Min(sortedFt.Count - 1, Math.Max(0, (int)Math.Floor(0.99 * (sortedFt.Count - 1))));
                double p99Ft = sortedFt[idx99];
                fps1PctLow = p99Ft > 0 ? 1000.0 / p99Ft : avgFps;
            }

            Color lightFill = Color.FromArgb(242, 244, 247);
            Color headerFill = Color.FromArgb(225, 230, 238);
            Color borderColor = Color.FromArgb(210, 215, 222);

            wsGraph.Cells[3, 1].Value = "Summary Metric";
            wsGraph.Cells[3, 2].Value = "Value";
            wsGraph.Row(3).Style.Font.Bold = true;
            wsGraph.Cells[3, 1, 3, 2].Style.Font.Bold = true;
            wsGraph.Cells[3, 1, 3, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            wsGraph.Cells[3, 1, 3, 2].Style.Fill.BackgroundColor.SetColor(headerFill);

            var summaryRows = new (string Label, double Value)[]
            {
                ("Average FPS", Math.Round(avgFps, 1)),
                ("Average frametime (ms)", Math.Round(avgFrametimeMs, 1)),
                ("1% low FPS (percentile)", Math.Round(fps1PctLow, 1))
            };

            for (int i = 0; i < summaryRows.Length; i++)
            {
                int r = 4 + i;
                wsGraph.Cells[r, 1].Value = summaryRows[i].Label;
                wsGraph.Cells[r, 1].Style.Font.Bold = true;
                wsGraph.Cells[r, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                wsGraph.Cells[r, 1].Style.Fill.BackgroundColor.SetColor(lightFill);

                wsGraph.Cells[r, 2].Value = summaryRows[i].Value;
                wsGraph.Cells[r, 2].Style.Numberformat.Format = "0.0";
                wsGraph.Cells[r, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
                wsGraph.Cells[r, 2].Style.Fill.BackgroundColor.SetColor(lightFill);
            }

            wsGraph.Cells[3, 1, 6, 2].Style.Border.Top.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 1, 6, 2].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 1, 6, 2].Style.Border.Left.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 1, 6, 2].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            wsGraph.Cells[3, 1, 6, 2].Style.Border.Top.Color.SetColor(borderColor);
            wsGraph.Cells[3, 1, 6, 2].Style.Border.Bottom.Color.SetColor(borderColor);
            wsGraph.Cells[3, 1, 6, 2].Style.Border.Left.Color.SetColor(borderColor);
            wsGraph.Cells[3, 1, 6, 2].Style.Border.Right.Color.SetColor(borderColor);

            wsGraph.Cells[3, 1, 6, 2].AutoFitColumns();

            if (hasFullSeconds && endRow >= 2)
            {
                var chart = wsGraph.Drawings.AddChart("FPSChart", eChartType.Line) as ExcelLineChart;
                chart.Title.Text = $"{gameProcessName} - FPS";
                chart.SetPosition(2, 0, 3, 0);
                chart.SetSize(900, 450);

                var ser1 = chart.Series.Add(wsLog.Cells[2, 4, endRow, 4], wsLog.Cells[2, 3, endRow, 3]) as ExcelLineChartSerie;
                ser1.Header = "Average FPS";
                ser1.LineWidth = 1.5;
                ser1.LineColor = Color.FromArgb(0, 114, 206);
                ser1.Marker = eMarkerStyle.None;

                var ser2 = chart.Series.Add(wsLog.Cells[2, 5, endRow, 5], wsLog.Cells[2, 3, endRow, 3]) as ExcelLineChartSerie;
                ser2.Header = "1% low";
                ser2.LineWidth = 1.5;
                ser2.LineColor = Color.FromArgb(220, 60, 50);
                ser2.Marker = eMarkerStyle.None;

                chart.YAxis.MinValue = 0.0;
                chart.YAxis.MajorGridlines.Fill.Style = eFillStyle.SolidFill;
                chart.YAxis.MajorGridlines.Fill.Color = Color.FromArgb(220, 220, 220);

                chart.XAxis.Border.Fill.Style = eFillStyle.SolidFill;
                chart.XAxis.Border.Fill.Color = Color.FromArgb(215, 215, 215);
                chart.YAxis.Border.Fill.Style = eFillStyle.SolidFill;
                chart.YAxis.Border.Fill.Color = Color.FromArgb(215, 215, 215);

                chart.XAxis.MajorUnit = 30.0;
                chart.XAxis.Format = "[mm]:ss";
                chart.Legend.Position = eLegendPosition.Bottom;
            }
        }
    }
}
