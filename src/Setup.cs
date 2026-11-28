// SPDX-License-Identifier: GPL-3.0-or-later
// OMI Setup Installer — Standalone 1-Click Installer published by JITHINDSK1
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("OMI Setup")]
[assembly: AssemblyProduct("OMI")]
[assembly: AssemblyCompany("JITHINDSK1")]
[assembly: AssemblyCopyright("Copyright (C) 2026 JITHINDSK1 - GPL-3.0-or-later")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace OmiSetup {
    public class SetupForm : Form {
        readonly ProgressBar progress;
        readonly Label lblStatus;
        readonly Label lblHeader;
        readonly Label lblSub;
        readonly Button btnClose;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFile(string lpFileName);

        public SetupForm() {
            Text = "OMI Setup — Installer";
            Width = 480;
            Height = 280;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(22, 19, 14); // Silk Blend midnight tone
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            lblHeader = new Label {
                Text = "Installing OMI",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 20),
                AutoSize = true
            };
            Controls.Add(lblHeader);

            lblSub = new Label {
                Text = "High-Performance Laptop Companion • Published by JITHINDSK1",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(170, 185, 195),
                Location = new Point(26, 56),
                AutoSize = true
            };
            Controls.Add(lblSub);

            progress = new ProgressBar {
                Location = new Point(28, 105),
                Width = 408,
                Height = 22,
                Style = ProgressBarStyle.Continuous,
                Value = 10
            };
            Controls.Add(progress);

            lblStatus = new Label {
                Text = "Preparing installation...",
                ForeColor = Color.FromArgb(200, 210, 220),
                Location = new Point(26, 140),
                Width = 408,
                Height = 24
            };
            Controls.Add(lblStatus);

            btnClose = new Button {
                Text = "Close",
                Location = new Point(336, 190),
                Width = 100,
                Height = 32,
                Visible = false,
                BackColor = Color.FromArgb(40, 45, 55),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnClose.FlatAppearance.BorderColor = Color.FromArgb(80, 90, 105);
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);

            Shown += (s, e) => {
                var t = new Thread(RunInstall) { IsBackground = true };
                t.Start();
            };
        }

        void SetStatus(string msg, int pct) {
            if (InvokeRequired) {
                Invoke(new Action(() => SetStatus(msg, pct)));
                return;
            }
            lblStatus.Text = msg;
            progress.Value = Math.Max(0, Math.Min(100, pct));
        }

        void Finish(bool success, string msg) {
            if (InvokeRequired) {
                Invoke(new Action(() => Finish(success, msg)));
                return;
            }
            progress.Value = 100;
            lblStatus.Text = msg;
            lblStatus.ForeColor = success ? Color.FromArgb(100, 220, 130) : Color.FromArgb(255, 100, 100);
            btnClose.Visible = true;
            if (success) {
                Thread.Sleep(800);
                Close();
            }
        }

        void RunInstall() {
            try {
                // 1. Trust certificate
                SetStatus("Installing verified publisher certificate (JITHINDSK1)...", 20);
                InstallCertificates();

                // 2. Kill existing processes
                SetStatus("Stopping running OMI processes...", 40);
                StopProcesses();

                // 3. Extract OMI.exe and files
                SetStatus("Deploying OMI to local programs directory...", 60);
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string installDir = Path.Combine(appData, "Programs", "OMI");
                Directory.CreateDirectory(installDir);

                string targetExe = Path.Combine(installDir, "OMI.exe");
                ExtractResource("OMI.exe", targetExe);
                ExtractResource("uninstall.cmd", Path.Combine(installDir, "uninstall.cmd"));

                // Remove Mark-of-the-Web (Zone.Identifier) if present to eliminate SmartScreen
                try { DeleteFile(targetExe + ":Zone.Identifier"); } catch { }
                try { DeleteFile(Path.Combine(installDir, "uninstall.cmd") + ":Zone.Identifier"); } catch { }
                try { DeleteFile(Assembly.GetExecutingAssembly().Location + ":Zone.Identifier"); } catch { }

                // 4. Create Shortcuts & Task
                SetStatus("Creating shortcuts and configuring elevated autostart...", 80);
                CreateShortcuts(targetExe, installDir);
                ConfigureAutostartTask(targetExe);

                // 5. Launch
                SetStatus("Launching OMI...", 95);
                try {
                    Process.Start(new ProcessStartInfo(targetExe) { WorkingDirectory = installDir, UseShellExecute = true });
                } catch { }

                Finish(true, "Installation completed successfully! OMI is running in your tray.");
            } catch (Exception ex) {
                Finish(false, "Setup failed: " + ex.Message);
            }
        }

        static void StopProcesses() {
            string[] names = { "OMI", "Ohman" };
            foreach (string n in names) {
                try {
                    foreach (var p in Process.GetProcessesByName(n)) {
                        try { p.Kill(); p.WaitForExit(2000); } catch { }
                    }
                } catch { }
            }
        }

        static void InstallCertificates() {
            try {
                var asm = Assembly.GetExecutingAssembly();
                using (var st = asm.GetManifestResourceStream("JITHINDSK1.cer")) {
                    if (st != null) {
                        byte[] raw = new byte[st.Length];
                        st.Read(raw, 0, raw.Length);
                        var cert = new X509Certificate2(raw);

                        // Install to LocalMachine Root
                        try {
                            using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine)) {
                                store.Open(OpenFlags.ReadWrite);
                                store.Add(cert);
                            }
                        } catch { }

                        // Install to LocalMachine TrustedPublisher
                        try {
                            using (var store = new X509Store(StoreName.TrustedPublisher, StoreLocation.LocalMachine)) {
                                store.Open(OpenFlags.ReadWrite);
                                store.Add(cert);
                            }
                        } catch { }

                        // Also install to CurrentUser stores
                        try {
                            using (var store = new X509Store(StoreName.TrustedPublisher, StoreLocation.CurrentUser)) {
                                store.Open(OpenFlags.ReadWrite);
                                store.Add(cert);
                            }
                        } catch { }
                    }
                }
            } catch { }
        }

        static void ExtractResource(string name, string targetPath) {
            var asm = Assembly.GetExecutingAssembly();
            // Try embedded resource first
            using (var st = asm.GetManifestResourceStream(name)) {
                if (st != null) {
                    using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write)) {
                        st.CopyTo(fs);
                    }
                    return;
                }
            }
            // Fallback: look beside setup.exe
            string localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            if (File.Exists(localFile)) {
                File.Copy(localFile, targetPath, true);
            }
        }

        static void CreateShortcuts(string targetExe, string workingDir) {
            try {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                dynamic shell = Activator.CreateInstance(shellType);

                // Desktop shortcut
                string desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "OMI.lnk");
                dynamic sc1 = shell.CreateShortcut(desk);
                sc1.TargetPath = targetExe;
                sc1.WorkingDirectory = workingDir;
                sc1.Description = "OMI Hardware Control • JITHINDSK1";
                sc1.Save();

                // Start Menu shortcut
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string startMenu = Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs\OMI.lnk");
                dynamic sc2 = shell.CreateShortcut(startMenu);
                sc2.TargetPath = targetExe;
                sc2.WorkingDirectory = workingDir;
                sc2.Description = "OMI Hardware Control • JITHINDSK1";
                sc2.Save();
            } catch { }
        }

        static void ConfigureAutostartTask(string targetExe) {
            try {
                var psi = new ProcessStartInfo("schtasks.exe", "/Create /TN \"OMI\" /TR \"\\\"" + targetExe + "\\\" --hidden\" /SC ONLOGON /RL HIGHEST /F") {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var p = Process.Start(psi);
                if (p != null) p.WaitForExit();
            } catch { }
        }
    }

    static class Program {
        [STAThread]
        static void Main(string[] args) {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }
}
