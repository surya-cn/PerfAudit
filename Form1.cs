using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace EA_Performance_Audit
{
    public partial class Form1 : Form
    {
        private Process _targetProcess;
        private TimeSpan _lastTotalProcessorTime;
        private DateTime _lastSampleTime;
        private List<string> _logEntries = new List<string>();
        private bool _isExported = false;

        public Form1()
        {
            InitializeComponent();
            this.Text = "PerfAudit v1.0";
            this.FormBorderStyle = FormBorderStyle.FixedSingle; // Disables resizing
            this.MaximizeBox = false; // Removes the maximize button
            this.StartPosition = FormStartPosition.CenterScreen; this.TopMost = true; // Keeps the tool on top of your game/test window
            RefreshProcessList();
            this.FormClosing += (s, e) => SafeExport("Manual App Closure");
        }

        private void btnRefresh_Click(object sender, EventArgs e) => RefreshProcessList();

        private void RefreshProcessList()
        {
            comboBoxProcesses.Items.Clear();
            var procs = Process.GetProcesses().OrderBy(p => p.ProcessName);
            foreach (var p in procs) comboBoxProcesses.Items.Add($"{p.ProcessName} (PID: {p.Id})");
        }

        private void btnToggle_Click(object sender, EventArgs e)
        {
            if (timer1.Enabled) SafeExport("Manual Stop");
            else StartMonitoring();
        }

        private void StartMonitoring()
        {
            try
            {
                string selected = comboBoxProcesses.SelectedItem.ToString();
                int pid = int.Parse(selected.Split(new[] { "PID: " }, StringSplitOptions.None)[1].Replace(")", ""));

                _targetProcess = Process.GetProcessById(pid);
                _targetProcess.EnableRaisingEvents = true;
                _targetProcess.Exited += (s, e) => SafeExport("Target Process Terminated");

                _lastTotalProcessorTime = _targetProcess.TotalProcessorTime;
                _lastSampleTime = DateTime.Now;
                _logEntries.Clear();
                _isExported = false;

                timer1.Start();
                lblStatus.Text = "Status: RECORDING...";
                lblStatus.ForeColor = System.Drawing.Color.LightGreen; // Set to Green immediately
                lblLiveUsage.ForeColor = System.Drawing.Color.LightGreen;
                btnToggle.Text = "Stop & Export";
                btnToggle.BackColor = System.Drawing.Color.LightCoral;
            }
            catch { MessageBox.Show("Please select a process first."); }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (_targetProcess == null || _targetProcess.HasExited) return;

            var currentTotalTime = _targetProcess.TotalProcessorTime;
            var currentSampleTime = DateTime.Now;

            double cpuUsedMs = (currentTotalTime - _lastTotalProcessorTime).TotalMilliseconds;
            double totalMsPassed = (currentSampleTime - _lastSampleTime).TotalMilliseconds;
            double cpuUsage = (cpuUsedMs / (Environment.ProcessorCount * totalMsPassed)) * 100;

            _lastTotalProcessorTime = currentTotalTime;
            _lastSampleTime = currentSampleTime;

            string line = $"{currentSampleTime:yyyy-MM-dd},{currentSampleTime:HH:mm:ss},{_targetProcess.ProcessName},{Math.Round(cpuUsage, 2)}";
            _logEntries.Add(line);

            // Live Update and Visual Warning
            lblLiveUsage.Text = $"CPU: {Math.Round(cpuUsage, 2)}%";
            if (cpuUsage > 15.0)
            {
                lblLiveUsage.ForeColor = System.Drawing.Color.Tomato;
                lblStatus.Text = "Status: WARNING - HIGH USAGE";
            }
            else
            {
                lblLiveUsage.ForeColor = System.Drawing.Color.LightGreen;
                lblStatus.Text = "Status: RECORDING...";
            }
        }

        private void SafeExport(string reason)
        {
            lock (this)
            {
                if (_isExported || _logEntries.Count == 0) return;
                _isExported = true;
                timer1.Stop();

                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"EA_Audit_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllLines(path, new[] { "Date,Time,Process,CPU%" }.Concat(_logEntries));

                if (this.InvokeRequired) this.BeginInvoke((Action)(() => ResetUI(reason)));
                else ResetUI(reason);
            }
        }

        private void ResetUI(string msg)
        {
            lblStatus.Text = $"Exported: {msg}";
            lblStatus.ForeColor = System.Drawing.Color.White; // Reset color
            lblLiveUsage.Text = "CPU: 0.00%";
            lblLiveUsage.ForeColor = System.Drawing.Color.White;
            btnToggle.Text = "Start Monitoring";
            btnToggle.BackColor = System.Drawing.Color.PaleGreen;
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }
    }
}