// SPDX-License-Identifier: GPL-3.0-or-later
// Ohman — Windows process monitor. Tracks all processes, detects resource spikes,
// maintains history of terminated processes and suspicious activity.
// All reads are best-effort; no exceptions propagate to callers.
// Target language level: C# 5 (in-box csc.exe of .NET Framework 4.8).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Ohman {

    /// <summary>Snapshot of one process at a point in time.</summary>
    public sealed class ProcessInfo {
        public int Pid;
        public string Name = "";
        public string Path = "";           // may be empty if access denied
        public string Publisher = "";      // from digital signature or version info
        public double CpuPct;              // 0..100
        public long RamMb;                 // working set MB
        public DateTime StartTime;
        public DateTime LastSeen;
        public DateTime EndTime;           // MinValue = still running
        public bool IsRunning;
        public string Dir = "";            // executable directory
        public bool InTempDir;             // running from %TEMP% or %APPDATA% or similar suspicious location
        public SignatureStatus Signature = SignatureStatus.Unknown;
        public bool IsSystemProcess;       // known OS process
        public int ParentPid;
        public string ParentName = "";
        public long DeltaCpuTicks;         // used internally for CPU calc
        public long PrevTotalCpuTicks;
        public string SafeDir { get { return Dir.Length > 0 ? Dir : "(unknown)"; } }
        public string Summary { get { return Name + " [" + Pid + "] " + (int)CpuPct + "% CPU " + RamMb + " MB"; } }
    }

    public enum SignatureStatus { Unknown, Signed, Unsigned, Invalid }

    /// <summary>A resource spike or suspicious-activity event.</summary>
    public sealed class ActivityAlert {
        public DateTime When;
        public AlertSeverity Severity;
        public string ProcessName = "";
        public int Pid;
        public string Path = "";
        public string Reason = "";           // human-readable description
        public string[] Indicators = new string[0]; // bullet points
        public bool Acknowledged;
    }

    public enum AlertSeverity { Info = 0, Low = 1, Medium = 2, High = 3, Critical = 4 }

    /// <summary>Settings for the process monitor (thresholds). Serialised via Settings.Apply.</summary>
    public sealed class MonitorSettings {
        public double CpuSpikeThreshold = 60.0;      // % CPU to flag a single process
        public long RamSpikeMb = 800;                 // MB RAM increase in one poll to flag
        public long RamHighMb = 2048;                 // absolute RAM threshold to flag
        public double GpuSpikeThreshold = 60.0;      // % GPU to flag (approximated)
        public bool AlertOnTempDir = true;            // flag processes in temp directories
        public bool AlertOnUnknownPublisher = false;  // flag unsigned executables (noisy)
        public int HistoryCapacity = 500;             // max terminated process records to keep
        public int AlertCapacity = 200;               // max alert records to keep
        public int PollMs = 3000;                     // process list polling interval
        public bool Enabled = true;
    }

    /// <summary>Lightweight Windows process monitor. Runs a background thread and fires events on the
    /// calling thread's dispatcher (or via the Updated event on a threadpool thread).</summary>
    public sealed class ProcessMonitor : IDisposable {
        // ---- public surface ----
        public event Action<List<ProcessInfo>, List<ActivityAlert>> Updated;  // current snapshot + new alerts
        public event Action<ProcessInfo> ProcessStarted;
        public event Action<ProcessInfo> ProcessEnded;

        readonly MonitorSettings cfg;
        readonly object sync = new object();
        readonly Dictionary<int, ProcessInfo> alive = new Dictionary<int, ProcessInfo>();
        readonly CircularBuffer<ProcessInfo> history;
        readonly CircularBuffer<ActivityAlert> alerts;
        Thread worker;
        volatile bool stop;
        volatile int intervalMs = 3000;
        readonly ManualResetEvent wake = new ManualResetEvent(false);
        // CPU tracking across polls
        DateTime lastPollTime = DateTime.MinValue;
        // known safe process name prefixes
        static readonly HashSet<string> SafeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "svchost","lsass","winlogon","csrss","smss","wininit","services","System","Idle","Registry",
            "MsMpEng","SecurityHealthService","WmiPrvSE","spoolsv","explorer","dwm","taskmgr","conhost",
            "RuntimeBroker","ShellExperienceHost","SearchHost","StartMenuExperienceHost","fontdrvhost",
            "audiodg","dllhost","msiexec","msdtc","vssvc","wuauclt","TrustedInstaller","tiworker",
            "NisSrv","MpCopyAccelerator","uhssvc","uhssvc64","nvcontainer","nvtelemetry","nvwmi","nvwm",
            "igfxCUIService","igfxEM","igfxEMN","HpqMAlrt","HpqSalut","HpqService","HpqMsgFX","HPWMISVC",
            "HPSupportSolutionsFrameworkService","hpqwmiex","Ohman","OmenControl"
        };
        static readonly HashSet<string> TempLike = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public ProcessMonitor(MonitorSettings settings = null) {
            cfg = settings ?? new MonitorSettings();
            history = new CircularBuffer<ProcessInfo>(cfg.HistoryCapacity);
            alerts = new CircularBuffer<ActivityAlert>(cfg.AlertCapacity);
            string tmp = Environment.GetEnvironmentVariable("TEMP") ?? "";
            string appd = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string lappd = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (tmp.Length > 0) TempLike.Add(tmp.ToLowerInvariant());
            if (appd.Length > 0) TempLike.Add(appd.ToLowerInvariant());
            if (lappd.Length > 0) TempLike.Add(lappd.ToLowerInvariant());
            TempLike.Add(Path.Combine(Path.GetTempPath()).ToLowerInvariant());
        }

        public void Start() {
            if (worker != null || !cfg.Enabled) return;
            worker = new Thread(Loop) { IsBackground = true, Name = "procmon" };
            worker.Start();
        }

        public void Stop() { stop = true; wake.Set(); }

        public void Dispose() { Stop(); }

        /// <summary>Snapshot of currently-alive processes (copy, thread-safe).</summary>
        public List<ProcessInfo> Current() {
            lock (sync) { return new List<ProcessInfo>(alive.Values); }
        }

        /// <summary>Recently terminated processes, most recent first.</summary>
        public List<ProcessInfo> History() { return history.ToList(); }

        /// <summary>Recent alerts, most recent first.</summary>
        public List<ActivityAlert> Alerts() { return alerts.ToList(); }

        public void ClearAlerts() { alerts.Clear(); }

        public MonitorSettings Settings { get { return cfg; } }

        public int UnacknowledgedAlerts { get { return alerts.CountWhere(delegate(ActivityAlert a) { return !a.Acknowledged; }); } }

        // ---- background loop ----
        void Loop() {
            Thread.Sleep(2000);  // let sensors start first
            while (!stop) {
                wake.Reset();
                try { Poll(); } catch (Exception ex) { Log.Write("procmon: " + ex.Message); }
                wake.WaitOne(intervalMs);
            }
        }

        Dictionary<int, long> prevCpuTime = new Dictionary<int, long>();
        DateTime prevPollAt = DateTime.MinValue;

        void Poll() {
            var now = DateTime.Now;
            double elapsed = prevPollAt == DateTime.MinValue ? 1.0 : (now - prevPollAt).TotalSeconds;
            prevPollAt = now;
            if (elapsed < 0.5) elapsed = 0.5;

            var procList = Process.GetProcesses();
            var seen = new HashSet<int>();
            var newlyStarted = new List<ProcessInfo>();
            var newAlerts = new List<ActivityAlert>();
            var snapshot = new List<ProcessInfo>(procList.Length);

            // Per-process CPU: compare total processor time
            int cpuCount = Math.Max(1, Environment.ProcessorCount);

            foreach (var p in procList) {
                try {
                    if (p.Id == 0) { p.Dispose(); continue; }  // System Idle
                    seen.Add(p.Id);
                    ProcessInfo info;
                    lock (sync) { alive.TryGetValue(p.Id, out info); }

                    bool isNew = info == null;
                    if (isNew) {
                        info = new ProcessInfo { Pid = p.Id, Name = p.ProcessName, IsRunning = true };
                        try { info.StartTime = p.StartTime; } catch { info.StartTime = now; }
                        try { info.Path = p.MainModule != null ? p.MainModule.FileName : ""; } catch { }
                        if (info.Path.Length > 0) {
                            info.Dir = Path.GetDirectoryName(info.Path) ?? "";
                            info.InTempDir = IsInTempDir(info.Path);
                            info.Publisher = GetPublisher(info.Path);
                            info.IsSystemProcess = SafeNames.Contains(info.Name);
                        } else {
                            info.IsSystemProcess = SafeNames.Contains(info.Name);
                        }
                        try { info.ParentPid = GetParentPid(p.Id); } catch { }
                    }

                    // CPU %
                    long curCpu = 0;
                    try { curCpu = p.TotalProcessorTime.Ticks; } catch { }
                    long prevCpu;
                    prevCpuTime.TryGetValue(p.Id, out prevCpu);
                    prevCpuTime[p.Id] = curCpu;
                    double cpuPct = prevCpu > 0 && curCpu > prevCpu
                        ? Math.Min(100.0 * (curCpu - prevCpu) / (elapsed * TimeSpan.TicksPerSecond * cpuCount), 100.0)
                        : 0.0;

                    // RAM
                    long ramMb = 0;
                    try { ramMb = p.WorkingSet64 / (1024 * 1024); } catch { }

                    // detect spikes
                    if (!isNew && !info.IsSystemProcess) {
                        CheckSpikes(info, cpuPct, ramMb, newAlerts);
                    }

                    info.CpuPct = cpuPct;
                    info.RamMb = ramMb;
                    info.LastSeen = now;

                    lock (sync) { alive[p.Id] = info; }
                    snapshot.Add(info);

                    if (isNew) newlyStarted.Add(info);
                } catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            // detect terminated processes
            List<ProcessInfo> ended = null;
            lock (sync) {
                var dead = new List<int>();
                foreach (var kv in alive) if (!seen.Contains(kv.Key)) dead.Add(kv.Key);
                if (dead.Count > 0) {
                    ended = new List<ProcessInfo>(dead.Count);
                    foreach (int pid in dead) {
                        var info = alive[pid];
                        info.IsRunning = false;
                        info.EndTime = now;
                        alive.Remove(pid);
                        history.Add(info);
                        ended.Add(info);
                        prevCpuTime.Remove(pid);
                    }
                }
            }

            // fire events
            if (newlyStarted.Count > 0) {
                foreach (var p in newlyStarted) { var h = ProcessStarted; if (h != null) try { h(p); } catch { } }
            }
            if (ended != null) {
                foreach (var p in ended) { var h = ProcessEnded; if (h != null) try { h(p); } catch { } }
            }

            // Add new alerts to store
            if (newAlerts.Count > 0) {
                foreach (var a in newAlerts) alerts.Add(a);
            }

            // Fire main updated event
            var upd = Updated;
            if (upd != null) try { upd(snapshot, newAlerts); } catch { }
        }

        void CheckSpikes(ProcessInfo info, double cpuPct, long ramMb, List<ActivityAlert> newAlerts) {
            var indicators = new List<string>();
            var severity = AlertSeverity.Info;

            if (cpuPct >= cfg.CpuSpikeThreshold) {
                indicators.Add("CPU usage " + (int)cpuPct + "% (threshold " + (int)cfg.CpuSpikeThreshold + "%)");
                severity = cpuPct >= 80 ? AlertSeverity.Medium : AlertSeverity.Low;
            }
            if (ramMb >= cfg.RamHighMb) {
                indicators.Add("RAM usage " + ramMb + " MB");
                if ((AlertSeverity)severity < AlertSeverity.Medium) severity = AlertSeverity.Low;
            }
            long prevRam = info.RamMb;
            if (ramMb - prevRam >= cfg.RamSpikeMb) {
                indicators.Add("Sudden RAM increase +" + (ramMb - prevRam) + " MB");
                severity = AlertSeverity.Medium;
            }
            if (cfg.AlertOnTempDir && info.InTempDir && !info.IsSystemProcess) {
                indicators.Add("Executable in suspicious directory: " + info.SafeDir);
                if ((AlertSeverity)severity < AlertSeverity.High) severity = AlertSeverity.High;
            }
            if (indicators.Count > 0) {
                // Don't spam: only alert once per 60 s per process
                lock (sync) {
                    if (ShouldSuppress(info.Pid)) return;
                    MarkSuppressed(info.Pid);
                }
                var alert = new ActivityAlert {
                    When = DateTime.Now,
                    Severity = severity,
                    ProcessName = info.Name,
                    Pid = info.Pid,
                    Path = info.Path,
                    Reason = "Unusual resource usage detected",
                    Indicators = indicators.ToArray()
                };
                newAlerts.Add(alert);
            }
        }

        // Suppression: don't generate more than one alert per process per 60 s
        readonly Dictionary<int, DateTime> suppressed = new Dictionary<int, DateTime>();
        bool ShouldSuppress(int pid) {
            DateTime t;
            if (!suppressed.TryGetValue(pid, out t)) return false;
            return (DateTime.Now - t).TotalSeconds < 60;
        }
        void MarkSuppressed(int pid) { suppressed[pid] = DateTime.Now; }

        static bool IsInTempDir(string path) {
            if (path.Length == 0) return false;
            string lower = path.ToLowerInvariant();
            foreach (string td in TempLike) {
                if (td.Length > 0 && lower.StartsWith(td, StringComparison.Ordinal)) return true;
            }
            // Also check if in %WINDIR%\Temp or any path containing "\\temp\\"
            return lower.Contains("\\temp\\") || lower.Contains("\\tmp\\") || lower.Contains("\\downloads\\");
        }

        static string GetPublisher(string path) {
            if (!File.Exists(path)) return "";
            try {
                var vi = FileVersionInfo.GetVersionInfo(path);
                string comp = vi.CompanyName ?? "";
                return comp.Length > 60 ? comp.Substring(0, 60) : comp;
            } catch { return ""; }
        }

        [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr h, int cls, ref PROCESS_BASIC_INFORMATION info, uint sz, out uint ret);
        [StructLayout(LayoutKind.Sequential)] struct PROCESS_BASIC_INFORMATION {
            public IntPtr Reserved1; public IntPtr PebBaseAddress; public IntPtr Reserved2a; public IntPtr Reserved2b;
            public IntPtr UniqueProcessId; public IntPtr InheritedFromUniqueProcessId;
        }
        static int GetParentPid(int pid) {
            try {
                using (var p = Process.GetProcessById(pid)) {
                    var info = new PROCESS_BASIC_INFORMATION();
                    uint ret;
                    if (NtQueryInformationProcess(p.Handle, 0, ref info, (uint)Marshal.SizeOf(info), out ret) == 0)
                        return info.InheritedFromUniqueProcessId.ToInt32();
                }
            } catch { }
            return 0;
        }
    }

    /// <summary>Fixed-capacity circular buffer; thread-safe.</summary>
    public sealed class CircularBuffer<T> {
        readonly T[] data;
        int head, count;
        readonly object sync = new object();
        public CircularBuffer(int capacity) { data = new T[Math.Max(1, capacity)]; }
        public void Add(T item) {
            lock (sync) {
                data[head % data.Length] = item;
                head++;
                if (count < data.Length) count++;
            }
        }
        public void Clear() {
            lock (sync) {
                head = 0;
                count = 0;
                Array.Clear(data, 0, data.Length);
            }
        }
        public List<T> ToList() {
            lock (sync) {
                var list = new List<T>(count);
                // Most recent first
                for (int i = count - 1; i >= 0; i--)
                    list.Add(data[(head - 1 - i + data.Length * 2) % data.Length]);
                return list;
            }
        }
        public int CountWhere(Func<T, bool> pred) {
            lock (sync) {
                int n = 0;
                for (int i = 0; i < count; i++) if (pred(data[(head - 1 - i + data.Length * 2) % data.Length])) n++;
                return n;
            }
        }
        public int Count { get { lock (sync) return count; } }
    }
}
