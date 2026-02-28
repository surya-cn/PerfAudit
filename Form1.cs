using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Timers;
using System.Windows.Forms;

namespace EA_Performance_Audit
{
    public partial class Form1 : Form
    {
        private Process _targetProcess;
        private System.Timers.Timer _precisionTimer;
        private Stopwatch _stopwatch = new Stopwatch();
        private TimeSpan _lastCpuTime;

        // Logic preserved from test phase
        private int _burstCounter = 0;
        private const int BurstThresholdSamples = 3;
        private const double CpuLimit = 15.0;

        private List<string> _logEntries = new List<string>();
        private bool _isExported = false;

        public Form1()
        {
            InitializeComponent();
            SetupFormAesthetics();

            // High-precision 100ms timer
            _precisionTimer = new System.Timers.Timer(100);
            _precisionTimer.AutoReset = true;
            _precisionTimer.Elapsed += OnPrecisionTimerElapsed;

            RefreshProcessList();
        }

        private void SetupFormAesthetics()
        {
            this.Text = "PerfAudit v1.6";
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.TopMost = chkAlwaysOnTop.Checked; // Preserved for ease of use during gaming
            this.BackColor = Color.FromArgb(32, 32, 32);
            lblLiveUsage.ForeColor = Color.LightGreen;
            lblStatus.ForeColor = Color.White;
        }

        private void chkAlwaysOnTop_CheckedChanged(object sender, EventArgs e) => this.TopMost = chkAlwaysOnTop.Checked;

        private void btnToggle_Click(object sender, EventArgs e)
        {
            if (_precisionTimer.Enabled)
            {
                _precisionTimer.Stop();
                SafeExport("Session Ended");
                UpdateUIStatus("IDLE", Color.White);
            }
            else
            {
                StartMonitoring();
            }
        }

        private void StartMonitoring()
        {
            try
            {
                if (comboBoxProcesses.SelectedItem == null) return;

                string selected = comboBoxProcesses.SelectedItem.ToString();
                int pid = int.Parse(selected.Split(new[] { "PID: " }, StringSplitOptions.None)[1].Replace(")", ""));

                _targetProcess = Process.GetProcessById(pid);
                _targetProcess.Refresh(); // Clear initial OS cache

                _lastCpuTime = _targetProcess.TotalProcessorTime;
                _stopwatch.Restart();
                _logEntries.Clear();
                _logEntries.Add("Timestamp,Process,CPU_Usage_Percent,Notes");
                _burstCounter = 0;
                _isExported = false;

                _precisionTimer.Start();
                UpdateUIStatus("RECORDING...", Color.LightGreen);
            }
            catch (Exception ex) { MessageBox.Show($"Target Error: {ex.Message}"); }
        }

        private void OnPrecisionTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_targetProcess == null || _targetProcess.HasExited) return;

            try
            {
                // BYPASS CACHE: Critical for catching micro-bursts
                _targetProcess.Refresh();

                double elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;
                _stopwatch.Restart();

                TimeSpan currentCpuTime = _targetProcess.TotalProcessorTime;
                double cpuUsedMs = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
                _lastCpuTime = currentCpuTime;

                // UNIVERSAL CPU LOGIC: Works on any core count
                int logicalCores = Environment.ProcessorCount;
                double cpuUsage = (cpuUsedMs / (logicalCores * elapsedMs)) * 100;
                if (cpuUsage > 100) cpuUsage = 100;

                string burstFlag = "";
                if (cpuUsage > CpuLimit)
                {
                    _burstCounter++;
                    // Flag triggers after 300ms of sustained high load
                    if (_burstCounter >= BurstThresholdSamples) burstFlag = "[BURST DETECTED]";
                }
                else { _burstCounter = 0; }

                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                _logEntries.Add($"{timestamp},{_targetProcess.ProcessName},{Math.Round(cpuUsage, 2)},{burstFlag}");

                this.BeginInvoke((Action)(() => {
                    lblLiveUsage.Text = $"CPU: {Math.Round(cpuUsage, 1)}%";
                    lblLiveUsage.ForeColor = (cpuUsage > CpuLimit) ? Color.Tomato : Color.LightGreen;
                    if (!string.IsNullOrEmpty(burstFlag)) lblStatus.Text = "Status: BURST ALERT!";
                }));
            }
            catch { }
        }

        private void SafeExport(string reason)
        {
            if (_isExported || _logEntries.Count <= 1) return;
            _isExported = true;

            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"Audit_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllLines(path, _logEntries);
                this.BeginInvoke((Action)(() => MessageBox.Show($"Log saved to Desktop: {reason}")));
            }
            catch { }
        }

        private void RefreshProcessList()
        {
            comboBoxProcesses.Items.Clear();
            var allProcesses = Process.GetProcesses().OrderBy(p => p.ProcessName).ToList();
            foreach (var p in allProcesses) comboBoxProcesses.Items.Add($"{p.ProcessName} (PID: {p.Id})");
        }

        private void UpdateUIStatus(string text, Color color)
        {
            if (this.InvokeRequired) this.Invoke((Action)(() => UpdateUIStatus(text, color)));
            else
            {
                lblStatus.Text = $"Status: {text}";
                lblStatus.ForeColor = color;
                btnToggle.Text = (text == "RECORDING...") ? "Stop & Export" : "Start Monitoring";
            }
        }
    }
}