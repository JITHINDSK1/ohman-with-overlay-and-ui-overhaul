// SPDX-License-Identifier: GPL-3.0-or-later
// Customizable Profiles System for Ohman:
// Allows creating, customizing, and hotkey-switching (Alt+W) between profiles with
// starting fan RPMs, performance modes, power gain, keyboard lighting, and
// automatic background/foreground process-triggered fan boosts.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Ohman {
    /// <summary>An application-specific auto-boost rule attached to a profile.</summary>
    public sealed class AppTrigger {
        public string ProcessName = ""; // e.g. "valorant", "blender"
        public string DisplayName = ""; // e.g. "VALORANT"
        public int TargetFanRpm = 57;   // Target fan level (e.g. 57 = 5700 RPM, 62 = 6200 RPM)
        public int TargetModeIndex = 3; // 0 Quiet, 1 Balanced, 2 Performance, 3 Unleashed
        public bool Enabled = true;

        public AppTrigger Clone() {
            return new AppTrigger {
                ProcessName = ProcessName,
                DisplayName = DisplayName,
                TargetFanRpm = TargetFanRpm,
                TargetModeIndex = TargetModeIndex,
                Enabled = Enabled
            };
        }
    }

    /// <summary>A complete user-customizable hardware profile.</summary>
    public sealed class UserProfile {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "New Profile";
        public int ModeIndex = 1;                     // 0 Quiet, 1 Balanced, 2 Performance, 3 Unleashed
        public FanMode FanMode = FanMode.Auto;        // Auto, Manual, Turbo6200
        public int FanRpm = 33;                       // 0 (Silent), 18, 23, 33, 48, 57, 62 (Heat Wipe)
        public int PowerGain = 0;                     // +0W to +15W
        public int LightEffect = 0;                   // 0 Static, 1 Breathe, 2 Cycle, 3 Wave, -1 Off
        public string LightHex = "#00E5FF";           // RGB hex code
        public int LightLevel = 100;                  // 0 - 100%
        public List<AppTrigger> Triggers = new List<AppTrigger>();

        public UserProfile Clone() {
            var p = new UserProfile {
                Id = Guid.NewGuid().ToString("N"),
                Name = Name + " (Copy)",
                ModeIndex = ModeIndex,
                FanMode = FanMode,
                FanRpm = FanRpm,
                PowerGain = PowerGain,
                LightEffect = LightEffect,
                LightHex = LightHex,
                LightLevel = LightLevel
            };
            foreach (var t in Triggers) p.Triggers.Add(t.Clone());
            return p;
        }

        public string FanDescription {
            get {
                if (FanMode == FanMode.Turbo6200 || FanRpm >= 62) return "Heat Wipe (6200 RPM)";
                if (FanMode == FanMode.Auto) return "Auto Curve";
                if (FanRpm == 0) return "0 RPM (Silent)";
                if (FanRpm >= 57) return "5700 RPM Turbo";
                return (FanRpm * 100) + " RPM";
            }
        }
    }

    /// <summary>Manager for loading, saving, cycling, and executing user profiles and app triggers.</summary>
    public sealed class ProfileManager {
        public static readonly ProfileManager Instance = new ProfileManager();

        public readonly List<UserProfile> Profiles = new List<UserProfile>();
        public int ActiveIndex = 0;
        public UserProfile ActiveProfile {
            get {
                if (Profiles.Count == 0) InitializeDefaults();
                if (ActiveIndex < 0 || ActiveIndex >= Profiles.Count) ActiveIndex = 0;
                return Profiles[ActiveIndex];
            }
        }

        readonly DispatcherTimer triggerTimer;
        Engine engine;
        Action<string, string, int, int> flashCallback;

        bool isAutoBoosted = false;
        string activeBoostProcess = null;
        int preBoostMode = 1;
        FanMode preBoostFanMode = FanMode.Auto;
        int preBoostFanRpm = 33;

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public event Action ProfilesChanged;
        public event Action ActiveProfileChanged;

        private ProfileManager() {
            triggerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            triggerTimer.Tick += OnTriggerTimerTick;
        }

        public void Initialize(Engine e, Action<string, string, int, int> flash) {
            engine = e;
            flashCallback = flash;
            Load();
            triggerTimer.Start();
        }

        public void InitializeDefaults() {
            Profiles.Clear();

            // 1. Gaming Profile
            var gaming = new UserProfile {
                Name = "Gaming",
                ModeIndex = 3, // Unleashed
                FanMode = FanMode.Manual,
                FanRpm = 57,   // 5700 RPM Turbo
                PowerGain = 15,
                LightEffect = 3, // Wave
                LightHex = "#9B5DE5",
                LightLevel = 100
            };
            gaming.Triggers.Add(new AppTrigger { ProcessName = "VALORANT-Win64-Shipping", DisplayName = "Valorant", TargetFanRpm = 57, TargetModeIndex = 3 });
            gaming.Triggers.Add(new AppTrigger { ProcessName = "cs2", DisplayName = "Counter-Strike 2", TargetFanRpm = 57, TargetModeIndex = 3 });
            Profiles.Add(gaming);

            // 2. Productivity / Coding Profile
            var work = new UserProfile {
                Name = "Productivity",
                ModeIndex = 1, // Balanced
                FanMode = FanMode.Auto,
                FanRpm = 33,
                PowerGain = 0,
                LightEffect = 0, // Static
                LightHex = "#00E5FF", // Cyan
                LightLevel = 80
            };
            work.Triggers.Add(new AppTrigger { ProcessName = "blender", DisplayName = "Blender", TargetFanRpm = 48, TargetModeIndex = 2 });
            Profiles.Add(work);

            // 3. Silent Night / Stealth
            var silent = new UserProfile {
                Name = "Silent Night",
                ModeIndex = 0, // Quiet
                FanMode = FanMode.Manual,
                FanRpm = 0,    // 0 RPM Silent
                PowerGain = 0,
                LightEffect = -1, // Off
                LightHex = "#FF8C00",
                LightLevel = 20
            };
            Profiles.Add(silent);

            // 4. Heat Wipe Overdrive
            var overdrive = new UserProfile {
                Name = "Heat Wipe",
                ModeIndex = 3, // Unleashed
                FanMode = FanMode.Turbo6200,
                FanRpm = 62,
                PowerGain = 15,
                LightEffect = 0,
                LightHex = "#FF0033", // Red
                LightLevel = 100
            };
            Profiles.Add(overdrive);

            ActiveIndex = 0;
            Save();
        }

        public void AddProfile(string name = null) {
            var p = new UserProfile {
                Name = string.IsNullOrEmpty(name) ? "Profile " + (Profiles.Count + 1) : name,
                ModeIndex = 1,
                FanMode = FanMode.Auto,
                FanRpm = 33,
                PowerGain = 0,
                LightEffect = 0,
                LightHex = "#00E5FF",
                LightLevel = 80
            };
            Profiles.Add(p);
            Save();
            if (ProfilesChanged != null) ProfilesChanged();
        }

        public void DeleteProfile(int index) {
            if (Profiles.Count <= 1) return; // Keep at least one profile
            if (index >= 0 && index < Profiles.Count) {
                Profiles.RemoveAt(index);
                if (ActiveIndex >= Profiles.Count) ActiveIndex = Profiles.Count - 1;
                Save();
                if (ProfilesChanged != null) ProfilesChanged();
                if (ActiveProfileChanged != null) ActiveProfileChanged();
            }
        }

        /// <summary>Alt+W Shortcut: Cycles to the next profile, applies it, and displays an on-screen HUD.</summary>
        public void CycleNextProfile() {
            if (Profiles.Count == 0) InitializeDefaults();
            ActiveIndex = (ActiveIndex + 1) % Profiles.Count;
            ApplyProfile(ActiveProfile, true);
            Save();
            if (ActiveProfileChanged != null) ActiveProfileChanged();
        }

        /// <summary>Selects and applies a profile by index.</summary>
        public void SelectProfile(int index) {
            if (index >= 0 && index < Profiles.Count) {
                ActiveIndex = index;
                ApplyProfile(ActiveProfile, true);
                Save();
                if (ActiveProfileChanged != null) ActiveProfileChanged();
            }
        }

        /// <summary>Applies all parameters of a profile to the hardware (mode, fan, power, lighting).</summary>
        public void ApplyProfile(UserProfile p, bool showFlash) {
            if (p == null || engine == null) return;
            try {
                // Reset any active auto-boost so profile's base settings cleanly take effect
                isAutoBoosted = false;
                activeBoostProcess = null;

                // 1. Performance Mode
                int mode = Math.Max(0, Math.Min(3, p.ModeIndex));
                engine.SetMode(mode, false);

                // 2. Power Gain (TGP / TDP)
                if (engine.P.HasPowerGain) {
                    engine.SetTdpOffset(Math.Max(0, Math.Min(15, p.PowerGain)), false);
                }

                // 3. Fan RPM / Fan Mode
                if (p.FanMode == FanMode.Turbo6200 || p.FanRpm >= 62) {
                    engine.SetFan(FanMode.Turbo6200, 62, 62, false);
                } else if (p.FanMode == FanMode.Auto) {
                    engine.SetFan(FanMode.Auto, 0, 0, false);
                } else {
                    int rpm = Math.Max(0, Math.Min(57, p.FanRpm));
                    engine.SetFan(FanMode.Manual, rpm, rpm, false);
                }

                // 4. Keyboard Lighting
                if (p.LightEffect == -1 || p.LightLevel <= 0) {
                    engine.SetLight(0, 0, false); // Turn off backlight
                } else {
                    Rgb col;
                    if (!Rgb.TryParse(p.LightHex.TrimStart('#'), out col)) col = new Rgb(0, 229, 255);
                    engine.SetLight(1, Math.Max(0, Math.Min(3, p.LightEffect)), false);
                    engine.SetLightLevel(Math.Max(0, Math.Min(100, p.LightLevel)));
                    engine.SetLightColor(null, col);
                }

                // 5. On-Screen Display HUD Notification
                if (showFlash && flashCallback != null) {
                    string detail = string.Format("{0} \u00B7 {1} \u00B7 +{2}W Gain",
                        Engine.ModeNames[mode], p.FanDescription, p.PowerGain);
                    flashCallback("Profile: " + p.Name, detail, mode, 3000);
                }
            } catch (Exception ex) {
                Log.Write("ApplyProfile error: " + ex.Message);
            }
        }

        /// <summary>Background tick checking for active applications registered in profile triggers.</summary>
        void OnTriggerTimerTick(object sender, EventArgs e) {
            if (engine == null || Profiles.Count == 0) return;
            var cur = ActiveProfile;
            if (cur == null || cur.Triggers == null || cur.Triggers.Count == 0) {
                if (isAutoBoosted) RestorePreBoost();
                return;
            }

            // Check foreground process
            string fgProcess = GetForegroundProcessName();

            AppTrigger matchedTrigger = null;
            if (!string.IsNullOrEmpty(fgProcess)) {
                foreach (var t in cur.Triggers) {
                    if (t.Enabled && string.Equals(t.ProcessName, fgProcess, StringComparison.OrdinalIgnoreCase)) {
                        matchedTrigger = t;
                        break;
                    }
                }
            }

            // Fallback: Check if any trigger process is actively running in background
            if (matchedTrigger == null) {
                foreach (var t in cur.Triggers) {
                    if (t.Enabled && IsProcessRunning(t.ProcessName)) {
                        matchedTrigger = t;
                        break;
                    }
                }
            }

            if (matchedTrigger != null) {
                if (!isAutoBoosted || !string.Equals(activeBoostProcess, matchedTrigger.ProcessName, StringComparison.OrdinalIgnoreCase)) {
                    // Activate Auto-Boost
                    if (!isAutoBoosted) {
                        preBoostMode = engine.ModeIndex;
                        preBoostFanMode = engine.S.Fan;
                        preBoostFanRpm = engine.S.Fan1;
                    }
                    isAutoBoosted = true;
                    activeBoostProcess = matchedTrigger.ProcessName;

                    // Apply boost targets
                    engine.SetMode(matchedTrigger.TargetModeIndex, false);
                    if (matchedTrigger.TargetFanRpm >= 62) {
                        engine.SetFan(FanMode.Turbo6200, 62, 62, false);
                    } else if (matchedTrigger.TargetFanRpm > 0) {
                        engine.SetFan(FanMode.Manual, matchedTrigger.TargetFanRpm, matchedTrigger.TargetFanRpm, false);
                    }

                    if (flashCallback != null) {
                        string name = string.IsNullOrEmpty(matchedTrigger.DisplayName) ? matchedTrigger.ProcessName : matchedTrigger.DisplayName;
                        flashCallback("Auto-Boost: " + name, "Fans boosted to " + (matchedTrigger.TargetFanRpm * 100) + " RPM", matchedTrigger.TargetModeIndex, 2500);
                    }
                }
            } else {
                if (isAutoBoosted) {
                    RestorePreBoost();
                }
            }
        }

        void RestorePreBoost() {
            isAutoBoosted = false;
            string prev = activeBoostProcess;
            activeBoostProcess = null;

            // Restore base profile settings
            if (ActiveProfile != null) {
                ApplyProfile(ActiveProfile, false);
            }

            if (flashCallback != null && !string.IsNullOrEmpty(prev)) {
                flashCallback("Auto-Boost Finished", "Restored " + ActiveProfile.Name + " (" + ActiveProfile.FanDescription + ")", ActiveProfile.ModeIndex, 2000);
            }
        }

        static string GetForegroundProcessName() {
            try {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return "";
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return "";
                using (var p = Process.GetProcessById((int)pid)) {
                    return p.ProcessName;
                }
            } catch {
                return "";
            }
        }

        static bool IsProcessRunning(string procName) {
            if (string.IsNullOrEmpty(procName)) return false;
            try {
                string stem = procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? procName.Substring(0, procName.Length - 4) : procName;
                var list = Process.GetProcessesByName(stem);
                return list != null && list.Length > 0;
            } catch {
                return false;
            }
        }

        #region File Persistence (profiles.json)
        static string ProfilesFile {
            get {
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles.json");
                try {
                    // Test write access
                    using (FileStream fs = File.Open(local, FileMode.OpenOrCreate, FileAccess.Write)) { }
                    return local;
                } catch {
                    string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ohman");
                    if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
                    return Path.Combine(appData, "profiles.json");
                }
            }
        }

        public void Save() {
            try {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"ActiveIndex\": " + ActiveIndex + ",");
                sb.AppendLine("  \"Profiles\": [");
                for (int i = 0; i < Profiles.Count; i++) {
                    var p = Profiles[i];
                    sb.AppendLine("    {");
                    sb.AppendLine("      \"Id\": \"" + EscapeJson(p.Id) + "\",");
                    sb.AppendLine("      \"Name\": \"" + EscapeJson(p.Name) + "\",");
                    sb.AppendLine("      \"ModeIndex\": " + p.ModeIndex + ",");
                    sb.AppendLine("      \"FanMode\": " + (int)p.FanMode + ",");
                    sb.AppendLine("      \"FanRpm\": " + p.FanRpm + ",");
                    sb.AppendLine("      \"PowerGain\": " + p.PowerGain + ",");
                    sb.AppendLine("      \"LightEffect\": " + p.LightEffect + ",");
                    sb.AppendLine("      \"LightHex\": \"" + EscapeJson(p.LightHex) + "\",");
                    sb.AppendLine("      \"LightLevel\": " + p.LightLevel + ",");
                    sb.AppendLine("      \"Triggers\": [");
                    for (int j = 0; j < p.Triggers.Count; j++) {
                        var t = p.Triggers[j];
                        sb.AppendLine("        {");
                        sb.AppendLine("          \"ProcessName\": \"" + EscapeJson(t.ProcessName) + "\",");
                        sb.AppendLine("          \"DisplayName\": \"" + EscapeJson(t.DisplayName) + "\",");
                        sb.AppendLine("          \"TargetFanRpm\": " + t.TargetFanRpm + ",");
                        sb.AppendLine("          \"TargetModeIndex\": " + t.TargetModeIndex + ",");
                        sb.AppendLine("          \"Enabled\": " + (t.Enabled ? "true" : "false"));
                        sb.Append("        }");
                        if (j < p.Triggers.Count - 1) sb.Append(",");
                        sb.AppendLine();
                    }
                    sb.AppendLine("      ]");
                    sb.Append("    }");
                    if (i < Profiles.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("  ]");
                sb.AppendLine("}");
                File.WriteAllText(ProfilesFile, sb.ToString(), Encoding.UTF8);
            } catch (Exception ex) {
                Log.Write("ProfileManager.Save failed: " + ex.Message);
            }
        }

        public void Load() {
            try {
                string file = ProfilesFile;
                if (!File.Exists(file)) {
                    InitializeDefaults();
                    return;
                }
                string json = File.ReadAllText(file, Encoding.UTF8);
                ParseJson(json);
                if (Profiles.Count == 0) InitializeDefaults();
            } catch (Exception ex) {
                Log.Write("ProfileManager.Load failed: " + ex.Message);
                InitializeDefaults();
            }
        }

        void ParseJson(string json) {
            Profiles.Clear();
            if (string.IsNullOrEmpty(json)) return;

            // Simple robust line-by-line / token parser
            UserProfile curP = null;
            AppTrigger curT = null;
            bool inTriggers = false;

            string[] lines = json.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines) {
                string l = rawLine.Trim();
                if (l.StartsWith("\"ActiveIndex\":", StringComparison.OrdinalIgnoreCase)) {
                    int idx;
                    if (int.TryParse(GetJsonVal(l), out idx)) ActiveIndex = idx;
                } else if (l.StartsWith("\"Triggers\":", StringComparison.OrdinalIgnoreCase)) {
                    inTriggers = true;
                } else if (l == "]" && inTriggers) {
                    inTriggers = false;
                } else if (l == "{" || l == "{") {
                    if (inTriggers) {
                        curT = new AppTrigger();
                    } else if (curP == null && l.Contains("{")) {
                        curP = new UserProfile();
                    }
                } else if (l.StartsWith("}") || l.StartsWith("},")) {
                    if (inTriggers && curT != null) {
                        if (curP != null && !string.IsNullOrEmpty(curT.ProcessName)) {
                            curP.Triggers.Add(curT);
                        }
                        curT = null;
                    } else if (!inTriggers && curP != null) {
                        Profiles.Add(curP);
                        curP = null;
                    }
                } else {
                    // Key-value parsing
                    if (inTriggers && curT != null) {
                        if (l.StartsWith("\"ProcessName\":", StringComparison.OrdinalIgnoreCase)) curT.ProcessName = GetJsonVal(l);
                        else if (l.StartsWith("\"DisplayName\":", StringComparison.OrdinalIgnoreCase)) curT.DisplayName = GetJsonVal(l);
                        else if (l.StartsWith("\"TargetFanRpm\":", StringComparison.OrdinalIgnoreCase)) { int v; if (int.TryParse(GetJsonVal(l), out v)) curT.TargetFanRpm = v; }
                        else if (l.StartsWith("\"TargetModeIndex\":", StringComparison.OrdinalIgnoreCase)) { int v; if (int.TryParse(GetJsonVal(l), out v)) curT.TargetModeIndex = v; }
                        else if (l.StartsWith("\"Enabled\":", StringComparison.OrdinalIgnoreCase)) { bool b; if (bool.TryParse(GetJsonVal(l), out b)) curT.Enabled = b; }
                    } else if (curP != null) {
                        if (l.StartsWith("\"Id\":", StringComparison.OrdinalIgnoreCase)) curP.Id = GetJsonVal(l);
                        else if (l.StartsWith("\"Name\":", StringComparison.OrdinalIgnoreCase)) curP.Name = GetJsonVal(l);
                        else if (l.StartsWith("\"ModeIndex\":", StringComparison.OrdinalIgnoreCase)) { int v; if (int.TryParse(GetJsonVal(l), out v)) curP.ModeIndex = v; }
                        else if (l.StartsWith("\"FanMode\":", StringComparison.OrdinalIgnoreCase)) { int v; if (int.TryParse(GetJsonVal(l), out v)) curP.FanMode = (FanMode)v; }
                        else if (l.StartsWith("\"FanRpm\":", StringComparison.OrdinalIgnoreCase)) { int v; if (int.TryParse(GetJsonVal(l), out v)) curP.FanRpm = v; }
                        else if (l.StartsWith("\"PowerGain\":", StringComparison.OrdinalIgnoreCase)) { int v; if (int.TryParse(GetJsonVal(l), out v)) curP.PowerGain = v; }
                        else if (l.StartsWith("\"LightEffect\":", StringComparison.OrdinalIgnoreCase)) { int v; if (int.TryParse(GetJsonVal(l), out v)) curP.LightEffect = v; }
                        else if (l.StartsWith("\"LightHex\":", StringComparison.OrdinalIgnoreCase)) curP.LightHex = GetJsonVal(l);
                        else if (l.StartsWith("\"LightLevel\":", StringComparison.OrdinalIgnoreCase)) { int v; if (int.TryParse(GetJsonVal(l), out v)) curP.LightLevel = v; }
                    }
                }
            }
        }

        static string GetJsonVal(string line) {
            int colon = line.IndexOf(':');
            if (colon < 0) return "";
            string val = line.Substring(colon + 1).Trim().TrimEnd(',');
            if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2) {
                return val.Substring(1, val.Length - 2);
            }
            return val;
        }

        static string EscapeJson(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
        #endregion
    }
}
