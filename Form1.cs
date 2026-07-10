using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Timers;
using System.Windows.Forms;

namespace PerfAudit
{
    public partial class Form1 : Form
    {
        private Process _targetProcess;
        private System.Timers.Timer _precisionTimer;
        private Stopwatch _stopwatch = new Stopwatch();
        private TimeSpan _lastCpuTime;

        private double _accumulatedBurstMs = 0;
        private const double BurstThresholdMs = 300;
        private double _currentThreshold = 15.0;

        private int _totalBurstCount = 0;
        private double _totalCpuSum = 0;
        private int _sampleCount = 0;

        private List<string> _logEntries = new List<string>();
        private bool _isExported = false;

        public Form1()
        {
            InitializeComponent();
            SetupFormAesthetics();

            _precisionTimer = new System.Timers.Timer(100);
            _precisionTimer.AutoReset = true;
            _precisionTimer.Elapsed += OnPrecisionTimerElapsed;

            RefreshProcessList();
            txtThreshold.Text = "15.0";
        }

        private void SetupFormAesthetics()
        {
            this.Text = "PerfAudit v1.7";
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.TopMost = chkAlwaysOnTop.Checked;
            this.BackColor = Color.FromArgb(32, 32, 32);
            lblLiveUsage.ForeColor = Color.LightGreen;
            lblStatus.ForeColor = Color.White;

            // Note: TopMost = true will not render above exclusive fullscreen applications 
            // (e.g. DirectX/DXGI) because they bypass the window manager's z-order.
            ToolTip tooltip = new ToolTip();
            tooltip.SetToolTip(chkAlwaysOnTop, "If UI is not visible, target may be running exclusive fullscreen — monitoring continues in background.");
        }

        private void chkAlwaysOnTop_CheckedChanged(object sender, EventArgs e) => this.TopMost = chkAlwaysOnTop.Checked;

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            RefreshProcessList();
        }

        private void btnToggle_Click(object sender, EventArgs e)
        {
            if (_precisionTimer.Enabled)
            {
                _precisionTimer.Stop();
                txtThreshold.Enabled = true;
                comboBoxProcesses.Enabled = true;
                SafeExport("Session Ended");
                UpdateUIStatus("IDLE", Color.White);
            }
            else
            {
                if (!double.TryParse(txtThreshold.Text, out _currentThreshold))
                {
                    MessageBox.Show("Invalid Threshold. Resetting to 15.0%", "Input Error");
                    _currentThreshold = 15.0;
                    txtThreshold.Text = "15.0";
                }
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
                _targetProcess.Refresh();

                _lastCpuTime = _targetProcess.TotalProcessorTime;
                _stopwatch.Restart();
                _logEntries.Clear();

                _totalBurstCount = 0;
                _totalCpuSum = 0;
                _sampleCount = 0;

                _logEntries.Add($"Timestamp,Process,CPU_Usage_Percent,Notes,Threshold_Used:{_currentThreshold}%");

                _accumulatedBurstMs = 0;
                _isExported = false;

                txtThreshold.Enabled = false;
                comboBoxProcesses.Enabled = false;
                _precisionTimer.Start();
                UpdateUIStatus($"RECORDING ({_currentThreshold}%) - If hidden, target may be in exclusive fullscreen", Color.LightGreen);
            }
            catch (Exception ex) { MessageBox.Show($"Target Error: {ex.Message}"); }
        }

        private void OnPrecisionTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_targetProcess == null || _targetProcess.HasExited)
            {
                _precisionTimer.Stop();
                SafeExport("Process Terminated");

                this.BeginInvoke((Action)(() => {
                    UpdateUIStatus("IDLE (Target Exited)", Color.Orange);
                    txtThreshold.Enabled = true;
                    comboBoxProcesses.Enabled = true;
                }));
                return;
            }

            try
            {
                _targetProcess.Refresh();
                double elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;
                _stopwatch.Restart();

                TimeSpan currentCpuTime = _targetProcess.TotalProcessorTime;
                double cpuUsedMs = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
                _lastCpuTime = currentCpuTime;

                int logicalCores = Environment.ProcessorCount;
                double cpuUsage = (cpuUsedMs / (logicalCores * elapsedMs)) * 100;
                if (cpuUsage > 100) cpuUsage = 100;

                string burstFlag = "";
                if (cpuUsage > _currentThreshold)
                {
                    _accumulatedBurstMs += elapsedMs;
                    if (_accumulatedBurstMs >= BurstThresholdMs)
                    {
                        burstFlag = "BURST DETECTED";
                        _totalBurstCount++;
                    }
                }
                else
                {
                    _accumulatedBurstMs = 0;
                }

                _totalCpuSum += cpuUsage;
                _sampleCount++;

                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

                _logEntries.Add($"' {timestamp},{_targetProcess.ProcessName},{Math.Round(cpuUsage, 2)},{burstFlag},");

                this.BeginInvoke((Action)(() => {
                    lblLiveUsage.Text = $"CPU: {Math.Round(cpuUsage, 1)}%";
                    lblLiveUsage.ForeColor = (cpuUsage > _currentThreshold) ? Color.Tomato : Color.LightGreen;
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
                double avgCpu = _sampleCount > 0 ? _totalCpuSum / _sampleCount : 0;
                _logEntries[0] = $"Timestamp,Process,CPU_Usage_Percent,Notes,Threshold_Used:{_currentThreshold}%,,TOTAL BURST SAMPLES: {_totalBurstCount},,AVG CPU: {Math.Round(avgCpu, 2)}%";

                _logEntries.Add(",,,,");

                _logEntries.Add($",,,TOTAL BURST SAMPLES:,{_totalBurstCount}");
                _logEntries.Add($",,,AVERAGE SESSION CPU:,{Math.Round(avgCpu, 2)}%");

                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"Audit_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllLines(path, _logEntries);
                this.BeginInvoke((Action)(() => MessageBox.Show($"Log saved to Desktop.\nThreshold used: {_currentThreshold}%")));
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
                btnToggle.Text = (text.StartsWith("RECORDING")) ? "Stop & Export" : "Start Monitoring";
            }
        }
    }
}