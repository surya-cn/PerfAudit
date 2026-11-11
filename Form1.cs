using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        private string _tempCpuFile;
        private StreamWriter _tempCpuWriter;
        private object _cpuLock = new object();
        private List<CpuSample> _cpuSamples;
        private long _cpuStartQpc;

        private FpsLogger _fpsLogger;
        private bool _cpuActive = false;
        private bool _fpsActive = false;
        private bool _cpuLogged = false;
        private bool _fpsLogged = false;
        private bool _isExporting = false;
        private ComboBoxTypeAhead _cpuTypeAhead;
        private ComboBoxTypeAhead _gameTypeAhead;

        public Form1()
        {
            InitializeComponent();
            SetupFormAesthetics();

            _precisionTimer = new System.Timers.Timer(100);
            _precisionTimer.AutoReset = true;
            _precisionTimer.Elapsed += OnPrecisionTimerElapsed;

            _cpuTypeAhead = ComboBoxTypeAhead.Attach(comboBoxProcesses);
            _gameTypeAhead = ComboBoxTypeAhead.Attach(comboBoxGame);

            FpsLogger.CleanupLeftoverSession();
            RefreshProcessList();
            txtThreshold.Text = "15.0";
        }

        private void SetupFormAesthetics()
        {
            this.Text = "PerfAudit v1.9";
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.TopMost = chkAlwaysOnTop.Checked;
            this.BackColor = Color.FromArgb(32, 32, 32);
            lblLiveUsage.ForeColor = Color.LightGreen;
            lblLiveFps.ForeColor = Color.Turquoise;
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
            if (_cpuActive || _fpsActive)
            {
                StopMonitoringAndExport("Session Ended");
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
            bool cpuSelected = comboBoxProcesses.SelectedIndex > 0 &&
                               comboBoxProcesses.SelectedItem != null &&
                               comboBoxProcesses.SelectedItem.ToString() != "None";

            bool gameSelected = comboBoxGame.SelectedIndex > 0 &&
                                comboBoxGame.SelectedItem != null &&
                                comboBoxGame.SelectedItem.ToString() != "None";

            if (!cpuSelected && !gameSelected)
            {
                MessageBox.Show("Please select a target process for CPU monitoring, a game for FPS monitoring, or both.", "No Logger Selected");
                return;
            }

            if (gameSelected)
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                {
                    MessageBox.Show("Administrator privileges are required for ETW FPS monitoring. Please run PerfAudit as administrator.", "Administrator Privileges Required");
                    return;
                }
            }

            int cpuPid = -1;
            if (cpuSelected)
            {
                try
                {
                    string selected = comboBoxProcesses.SelectedItem.ToString();
                    cpuPid = int.Parse(selected.Split(new[] { "PID: " }, StringSplitOptions.None)[1].Replace(")", ""));
                    _targetProcess = Process.GetProcessById(cpuPid);
                    _targetProcess.Refresh();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to attach to CPU target process: {ex.Message}", "Process Error");
                    return;
                }
            }
            else
            {
                _targetProcess = null;
            }

            long startQpc = Stopwatch.GetTimestamp();
            DateTime sessionStart = DateTime.Now;

            _cpuActive = false;
            _fpsActive = false;
            _cpuLogged = false;
            _fpsLogged = false;

            if (cpuSelected)
            {
                try
                {
                    _tempCpuFile = Path.GetTempFileName();
                    _tempCpuWriter = new StreamWriter(new FileStream(_tempCpuFile, FileMode.Create, FileAccess.Write, FileShare.Read, 32768), Encoding.UTF8);

                    _cpuSamples = new List<CpuSample>();
                    _cpuStartQpc = startQpc;

                    _lastCpuTime = _targetProcess.TotalProcessorTime;
                    _stopwatch.Restart();

                    _totalBurstCount = 0;
                    _totalCpuSum = 0;
                    _sampleCount = 0;
                    _accumulatedBurstMs = 0;

                    _cpuActive = true;
                    _cpuLogged = true;
                    _precisionTimer.Start();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to initialize CPU logging: {ex.Message}", "CPU Logger Error");
                    _cpuActive = false;
                }
            }

            if (gameSelected)
            {
                string gameName = comboBoxGame.SelectedItem.ToString();
                _fpsLogger = new FpsLogger();
                _fpsLogger.FpsUpdated += (fps) =>
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        lblLiveFps.Text = $"FPS: {fps}";
                    }));
                };
                _fpsLogger.DiagnosticsUpdated += (diag) =>
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        if (_fpsActive)
                        {
                            if (!string.IsNullOrEmpty(_fpsLogger?.EtwThreadException))
                            {
                                lblStatus.Text = $"Status: {_fpsLogger.EtwThreadException.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()}";
                                lblStatus.ForeColor = Color.Tomato;
                            }
                            else if (_cpuActive)
                            {
                                lblStatus.Text = $"Status: RECORDING (CPU: {_targetProcess?.ProcessName}, FPS: {_fpsLogger.ProcessName}[{_fpsLogger.ResolvedTargetPid}]) | {diag}";
                                lblStatus.ForeColor = Color.LightGreen;
                            }
                            else
                            {
                                lblStatus.Text = $"Status: RECORDING FPS ({_fpsLogger.ProcessName}[{_fpsLogger.ResolvedTargetPid}]) | {diag}";
                                lblStatus.ForeColor = Color.LightGreen;
                            }
                        }
                    }));
                };
                _fpsLogger.ProcessExited += () =>
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        OnGameProcessExited();
                    }));
                };

                if (_fpsLogger.Start(gameName, startQpc, out string fpsError))
                {
                    _fpsActive = true;
                    _fpsLogged = true;
                }
                else
                {
                    MessageBox.Show(fpsError ?? "Failed to start FPS monitoring.", "FPS Logger Error");
                    _fpsLogger.Dispose();
                    _fpsLogger = null;
                    _fpsActive = false;
                }
            }

            if (!_cpuActive && !_fpsActive)
            {
                return;
            }

            txtThreshold.Enabled = false;
            comboBoxProcesses.Enabled = false;
            comboBoxGame.Enabled = false;
            btnToggle.Text = "Stop & Export";

            string statusMessage;
            if (_cpuActive && _fpsActive)
                statusMessage = $"RECORDING (CPU: {_targetProcess.ProcessName}, FPS: {_fpsLogger.ProcessName})";
            else if (_cpuActive)
                statusMessage = $"RECORDING CPU ({_targetProcess.ProcessName} @ {_currentThreshold}%)";
            else
                statusMessage = $"RECORDING FPS ({_fpsLogger.ProcessName})";

            UpdateUIStatus($"{statusMessage} - If hidden, target may be in exclusive fullscreen", Color.LightGreen);
        }

        private void OnPrecisionTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_targetProcess == null || _targetProcess.HasExited)
            {
                _precisionTimer.Stop();
                _cpuActive = false;

                lock (_cpuLock)
                {
                    try { _tempCpuWriter?.Flush(); } catch { }
                }

                this.BeginInvoke((Action)(() =>
                {
                    lblLiveUsage.Text = "CPU: Exited";
                    lblLiveUsage.ForeColor = Color.Orange;

                    if (_fpsActive)
                    {
                        UpdateUIStatus("RECORDING (FPS only - CPU Target Exited)", Color.Orange);
                    }
                    else
                    {
                        StopMonitoringAndExport("CPU Process Terminated");
                    }
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

                long nowQpc = Stopwatch.GetTimestamp();
                double timeS = (double)(nowQpc - _cpuStartQpc) / Stopwatch.Frequency;
                if (timeS < 0) timeS = 0;

                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

                lock (_cpuLock)
                {
                    _cpuSamples?.Add(new CpuSample
                    {
                        TimeS = timeS,
                        CpuUsage = cpuUsage,
                        IsBurst = !string.IsNullOrEmpty(burstFlag)
                    });
                    _tempCpuWriter?.WriteLine($"' {timestamp},{_targetProcess.ProcessName},{Math.Round(cpuUsage, 2)},{burstFlag},");
                }

                this.BeginInvoke((Action)(() =>
                {
                    lblLiveUsage.Text = $"CPU: {Math.Round(cpuUsage, 1)}%";
                    lblLiveUsage.ForeColor = (cpuUsage > _currentThreshold) ? Color.Tomato : Color.LightGreen;
                    if (!string.IsNullOrEmpty(burstFlag))
                    {
                        string diag = (_fpsActive && _fpsLogger != null) ? $" | {_fpsLogger.GetDiagnosticSummary()}" : "";
                        lblStatus.Text = $"Status: BURST ALERT!{diag}";
                    }
                }));
            }
            catch { }
        }

        private void OnGameProcessExited()
        {
            _fpsActive = false;
            lblLiveFps.Text = "FPS: Exited";

            if (_cpuActive)
            {
                UpdateUIStatus($"RECORDING (CPU only - Game Exited)", Color.Orange);
            }
            else
            {
                StopMonitoringAndExport("Game Exited");
            }
        }

        private void StopMonitoringAndExport(string reason)
        {
            if (_isExporting) return;
            _isExporting = true;

            _precisionTimer.Stop();
            _cpuActive = false;

            if (_fpsLogger != null)
            {
                _fpsLogger.Stop();
                _fpsActive = false;
            }

            lock (_cpuLock)
            {
                try
                {
                    _tempCpuWriter?.Flush();
                    _tempCpuWriter?.Close();
                    _tempCpuWriter = null;
                }
                catch { }
            }

            UpdateUIStatus("Saving report...", Color.Yellow);

            string outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"PerfAudit_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            string cpuFile = _tempCpuFile;
            List<FpsFrame> fpsFrames = _fpsLogger?.Frames;
            double threshold = _currentThreshold;
            int totalBursts = _totalBurstCount;
            double avgCpu = _sampleCount > 0 ? _totalCpuSum / _sampleCount : 0;
            string cpuProcessName = _targetProcess?.ProcessName;
            string gameName = _fpsLogger?.ProcessName;
            int totalFrames = _fpsLogger?.TotalFramesRecorded ?? 0;
            bool cpuLogged = _cpuLogged;
            bool fpsLogged = _fpsLogged;

            List<CpuSample> cpuSamples = null;
            lock (_cpuLock)
            {
                cpuSamples = _cpuSamples;
                _cpuSamples = null;
            }

            Task.Run(() =>
            {
                try
                {
                    ExcelReportGenerator.GenerateReport(
                        outputPath,
                        cpuLogged ? cpuFile : null,
                        cpuLogged ? cpuSamples : null,
                        cpuLogged ? cpuProcessName : null,
                        threshold,
                        totalBursts,
                        avgCpu,
                        fpsLogged ? fpsFrames : null,
                        gameName);

                    this.BeginInvoke((Action)(() =>
                    {
                        UpdateUIStatus("IDLE", Color.White);
                        txtThreshold.Enabled = true;
                        comboBoxProcesses.Enabled = true;
                        comboBoxGame.Enabled = true;
                        btnToggle.Text = "Start Monitoring";

                        var sb = new StringBuilder();
                        sb.AppendLine($"Report saved to Desktop:\n{Path.GetFileName(outputPath)}");
                        if (cpuLogged)
                        {
                            sb.AppendLine($"\n[CPU Monitoring]");
                            sb.AppendLine($"Threshold used: {threshold}%");
                            sb.AppendLine($"Total Burst Samples: {totalBursts}");
                            sb.AppendLine($"Average CPU: {Math.Round(avgCpu, 2)}%");
                        }
                        if (fpsLogged)
                        {
                            sb.AppendLine($"\n[FPS Monitoring]");
                            sb.AppendLine($"Game Process: {gameName}");
                            sb.AppendLine($"Total Frames: {totalFrames}");
                        }
                        MessageBox.Show(sb.ToString(), "Monitoring Complete");
                    }));
                }
                catch (Exception ex)
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        UpdateUIStatus($"Export Error: {ex.Message}", Color.Tomato);
                        txtThreshold.Enabled = true;
                        comboBoxProcesses.Enabled = true;
                        comboBoxGame.Enabled = true;
                        btnToggle.Text = "Start Monitoring";
                        MessageBox.Show($"Failed to generate Excel report: {ex.Message}", "Export Error");
                    }));
                }
                finally
                {
                    try { if (!string.IsNullOrEmpty(cpuFile) && File.Exists(cpuFile)) File.Delete(cpuFile); } catch { }

                    if (_fpsLogger != null)
                    {
                        _fpsLogger.Dispose();
                        _fpsLogger = null;
                    }
                    _isExporting = false;
                }
            });
        }

        private void RefreshProcessList()
        {
            comboBoxProcesses.Items.Clear();
            comboBoxProcesses.Items.Add("None");

            comboBoxGame.Items.Clear();
            comboBoxGame.Items.Add("None");

            var allProcesses = Process.GetProcesses().OrderBy(p => p.ProcessName).ToList();
            var distinctGameNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in allProcesses)
            {
                comboBoxProcesses.Items.Add($"{p.ProcessName} (PID: {p.Id})");
                if (distinctGameNames.Add(p.ProcessName))
                {
                    comboBoxGame.Items.Add(p.ProcessName);
                }
            }

            comboBoxProcesses.SelectedIndex = 0;
            comboBoxGame.SelectedIndex = 0;

            _cpuTypeAhead?.ClearBuffer();
            _gameTypeAhead?.ClearBuffer();
        }

        private void UpdateUIStatus(string text, Color color)
        {
            if (this.InvokeRequired) this.Invoke((Action)(() => UpdateUIStatus(text, color)));
            else
            {
                lblStatus.Text = $"Status: {text}";
                lblStatus.ForeColor = color;
                btnToggle.Text = (_cpuActive || _fpsActive) ? "Stop & Export" : "Start Monitoring";
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try { _precisionTimer?.Stop(); } catch { }
            try { _fpsLogger?.Stop(); } catch { }
            try { _fpsLogger?.Dispose(); } catch { }
            FpsLogger.CleanupLeftoverSession();
        }
    }
}
