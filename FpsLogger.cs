using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace PerfAudit
{
    public struct FrameSample
    {
        public long TimestampQpc;
        public DateTime WallClock;
    }

    public class FpsLogger : IDisposable
    {
        public const string SessionName = "PerfAuditFpsSession";
        private static readonly Guid DxgiGuid = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");

        private TraceEventSession _session;
        private BlockingCollection<FrameSample> _sampleQueue;
        private Task _processingTask;
        private Task _writerTask;
        private CancellationTokenSource _cts;

        private int _targetPid;
        private string _processName;
        private long _startQpc;
        private string _tempFilePath;

        private Process _gameProcess;
        private System.Timers.Timer _heartbeatTimer;
        private int _latestFps;
        private long _lastFrameTicks;

        public bool IsRunning { get; private set; }
        public string TempFilePath => _tempFilePath;
        public string ProcessName => _processName;
        public int TotalFramesRecorded { get; private set; }

        public long TotalEventsReceived;
        public long DxgiEventsReceived;
        public long TargetPidEventsReceived;
        public long EnqueuedPresentsCount;
        public string EtwThreadException { get; private set; }
        public int ResolvedTargetPid => _targetPid;
        public string ResolvedProcessName => _processName;

        public event Action<int> FpsUpdated;
        public event Action<string> DiagnosticsUpdated;
        public event Action ProcessExited;

        public static void CleanupLeftoverSession()
        {
            try
            {
                var active = TraceEventSession.GetActiveSessionNames();
                if (active != null && active.Contains(SessionName))
                {
                    using (var existing = TraceEventSession.GetActiveSession(SessionName))
                    {
                        existing?.Stop(true);
                    }
                }
            }
            catch { }
        }

        public bool Start(string processName, long startQpc, out string errorMessage)
        {
            errorMessage = null;
            if (IsRunning) return true;

            try
            {
                var matching = Process.GetProcessesByName(processName);
                if (matching == null || matching.Length == 0)
                {
                    errorMessage = $"No running process found matching '{processName}'.";
                    return false;
                }

                _gameProcess = matching.OrderByDescending(p =>
                {
                    try { return p.WorkingSet64; } catch { return 0L; }
                }).First();

                _targetPid = _gameProcess.Id;
                _processName = _gameProcess.ProcessName;
                _startQpc = startQpc;

                CleanupLeftoverSession();

                _tempFilePath = Path.GetTempFileName();
                _sampleQueue = new BlockingCollection<FrameSample>(20000);
                _cts = new CancellationTokenSource();
                TotalFramesRecorded = 0;
                TotalEventsReceived = 0;
                DxgiEventsReceived = 0;
                TargetPidEventsReceived = 0;
                EnqueuedPresentsCount = 0;
                EtwThreadException = null;

                _session = new TraceEventSession(SessionName);
                _session.StopOnDispose = true;

                // Enable DXGI provider with all keywords at Verbose level
                _session.EnableProvider(DxgiGuid, TraceEventLevel.Verbose, ulong.MaxValue);

                // Subscribe to raw stream (Source.AllEvents)
                _session.Source.AllEvents += OnTraceEvent;

                _processingTask = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        _session.Source.Process();
                        if (IsRunning && EtwThreadException == null)
                        {
                            EtwThreadException = "ETW pump exited unexpectedly (Process() returned).";
                        }
                    }
                    catch (Exception ex)
                    {
                        EtwThreadException = ex.ToString();
                    }
                }, TaskCreationOptions.LongRunning);

                _writerTask = Task.Factory.StartNew(DrainQueueToTempFile, TaskCreationOptions.LongRunning);

                _latestFps = 0;
                _lastFrameTicks = Environment.TickCount;

                _heartbeatTimer = new System.Timers.Timer(1000);
                _heartbeatTimer.AutoReset = true;
                _heartbeatTimer.Elapsed += (s, e) =>
                {
                    try
                    {
                        if (_gameProcess == null || _gameProcess.HasExited)
                        {
                            _heartbeatTimer?.Stop();
                            Stop();
                            ProcessExited?.Invoke();
                            return;
                        }

                        if (Environment.TickCount - _lastFrameTicks > 1500)
                        {
                            _latestFps = 0;
                        }

                        FpsUpdated?.Invoke(_latestFps);
                        DiagnosticsUpdated?.Invoke(GetDiagnosticSummary());
                    }
                    catch { }
                };
                _heartbeatTimer.Start();

                IsRunning = true;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                Stop();
                return false;
            }
        }

        private void OnTraceEvent(TraceEvent data)
        {
            Interlocked.Increment(ref TotalEventsReceived);

            if (data.ProviderGuid == DxgiGuid)
            {
                Interlocked.Increment(ref DxgiEventsReceived);
            }

            if (data.ProcessID == _targetPid)
            {
                Interlocked.Increment(ref TargetPidEventsReceived);

                int eventId = (int)data.ID;

                // Authoritative DXGI Present events: 42 (Present_Start) and 55 (PresentMultiplaneOverlay_Start)
                if (data.ProviderGuid == DxgiGuid && (eventId == 42 || eventId == 55))
                {
                    Interlocked.Increment(ref EnqueuedPresentsCount);
#pragma warning disable 618
                    _sampleQueue.TryAdd(new FrameSample
                    {
                        TimestampQpc = data.TimeStampQPC,
                        WallClock = data.TimeStamp
                    });
#pragma warning restore 618
                }
            }
        }

        private void DrainQueueToTempFile()
        {
            try
            {
                using (var stream = new FileStream(_tempFilePath, FileMode.Create, FileAccess.Write, FileShare.Read, 65536))
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    writer.WriteLine("Timestamp,Process,time_s,frametime_ms,fps_1s");

                    long lastQpc = 0;
                    long frequency = Stopwatch.Frequency;
                    var window = new Queue<double>();
                    long lastFlushTicks = Environment.TickCount;

                    foreach (var sample in _sampleQueue.GetConsumingEnumerable())
                    {
                        long currentQpc = sample.TimestampQpc;
                        double timeS = (double)(currentQpc - _startQpc) / frequency;
                        if (timeS < 0) timeS = 0;

                        double frameTimeMs = lastQpc == 0 ? 0.0 : (double)(currentQpc - lastQpc) * 1000.0 / frequency;
                        lastQpc = currentQpc;

                        window.Enqueue(timeS);
                        while (window.Count > 0 && window.Peek() < timeS - 1.0)
                        {
                            window.Dequeue();
                        }
                        int fps1s = window.Count;
                        _latestFps = fps1s;
                        _lastFrameTicks = Environment.TickCount;
                        TotalFramesRecorded++;

                        writer.WriteLine($"{sample.WallClock:HH:mm:ss.fff},{_processName},{timeS:F3},{frameTimeMs:F3},{fps1s}");

                        long now = Environment.TickCount;
                        if (now - lastFlushTicks >= 1000)
                        {
                            writer.Flush();
                            lastFlushTicks = now;
                        }
                    }

                    writer.Flush();
                }
            }
            catch { }
        }

        public string GetDiagnosticSummary()
        {
            if (!string.IsNullOrEmpty(EtwThreadException))
            {
                string firstLine = EtwThreadException.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return $"ETW Error: {firstLine}";
            }

            return $"Evts:{TotalEventsReceived} | DXGI:{DxgiEventsReceived} | PID({_targetPid}):{TargetPidEventsReceived} | Enq:{EnqueuedPresentsCount}";
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;

            try { _heartbeatTimer?.Stop(); } catch { }
            try { _heartbeatTimer?.Dispose(); } catch { }
            _heartbeatTimer = null;

            try { _cts?.Cancel(); } catch { }

            try
            {
                if (_session != null)
                {
                    _session.Stop(true);
                }
            }
            catch { }

            try
            {
                _sampleQueue?.CompleteAdding();
            }
            catch { }

            try
            {
                _writerTask?.Wait(2000);
            }
            catch { }

            try
            {
                _processingTask?.Wait(1000);
            }
            catch { }

            try
            {
                _session?.Dispose();
            }
            catch { }
            _session = null;
        }

        public void Dispose()
        {
            Stop();
            try { _cts?.Dispose(); } catch { }
            try { _sampleQueue?.Dispose(); } catch { }
        }
    }
}
