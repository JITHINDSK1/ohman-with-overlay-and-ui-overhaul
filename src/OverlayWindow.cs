// SPDX-License-Identifier: GPL-3.0-or-later
// Ohman — performance overlay window.
// Compact view: CPU / GPU telemetry + actual fan RPM.
// Expanded view: manual fan-speed sliders with explicit Apply and Auto buttons,
//   actual vs. target RPM, thermal-guard status.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Ohman {

    public sealed class OverlayWindow : Window {

        // -- engine / sensors -----------------------------------------------
        readonly Engine  E;
        readonly Sensors sensors;

        // -- compact telemetry labels ----------------------------------------
        TextBlock cpuPct, cpuTemp, gpuPct, gpuTemp, fanCompact;

        // -- expanded-panel controls ------------------------------------------
        StackPanel fanPanel;
        bool       fanPanelOpen;
        Border     fanToggleBtn;

        // fan 1 (CPU)
        Slider    sl1;
        TextBlock targetTxt1, actualTxt1;

        // fan 2 (GPU) -- hidden when FanCount == 1
        Slider    sl2;
        TextBlock targetTxt2, actualTxt2;
        StackPanel gpu2Row;

        // status / feedback line
        TextBlock statusTxt;

        // -- Win32 topmost / dragging / non-activating HUD --------------------
        bool     pinned, isClosing;
        IntPtr   hwnd = IntPtr.Zero, fgHook = IntPtr.Zero;
        HwndSource hwndSource;
        WinEventDelegate fgProc;
        DispatcherTimer zGuardTimer;

        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        static readonly IntPtr HWND_TOPMOST   = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        const int  GWL_EXSTYLE      = -20;
        const int  WS_EX_TOOLWINDOW = 0x00000080;
        const int  WS_EX_TOPMOST    = 0x00000008;
        const int  WS_EX_NOACTIVATE = 0x08000000;
        const uint GW_HWNDPREV      = 3;
        const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        const uint WINEVENT_OUTOFCONTEXT   = 0x0000;
        const uint SWP_NOSIZE     = 0x0001;
        const uint SWP_NOMOVE     = 0x0002;
        const uint SWP_NOZORDER   = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;
        const int  WM_MOUSEACTIVATE     = 0x0021;
        const int  MA_NOACTIVATE        = 3;
        const int  WM_WINDOWPOSCHANGING = 0x0046;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct WINDOWPOS {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        // -- palette ---------------------------------------------------------
        static readonly Brush BgBrush     = Freeze(new SolidColorBrush(Color.FromArgb(228, 13, 15, 20)));
        static readonly Brush BorderBrush_ = Freeze(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)));
        static readonly Brush AccentBrush = Freeze(new SolidColorBrush(Color.FromRgb(59, 130, 246)));
        static readonly Brush DimBrush    = Freeze(new SolidColorBrush(Color.FromRgb(148, 163, 184)));
        static readonly Brush WarnBrush   = Freeze(new SolidColorBrush(Color.FromRgb(245, 158, 11)));
        static readonly Brush SepBrush    = Freeze(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)));
        static readonly Brush BtnBgBrush  = Freeze(new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)));
        static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        static readonly FontFamily UiFont   = new FontFamily("Segoe UI");
        static readonly FontFamily MonoFont = new FontFamily("Consolas");

        // ====================================================================
        public OverlayWindow(Engine engine, Sensors sensors) {
            E            = engine;
            this.sensors = sensors;

            Title              = "Ohman Overlay";
            WindowStyle        = WindowStyle.None;
            AllowsTransparency = true;
            Background         = Brushes.Transparent;
            ShowActivated      = false;
            ShowInTaskbar      = false;
            Focusable          = false;
            ResizeMode         = ResizeMode.NoResize;
            SizeToContent      = SizeToContent.WidthAndHeight;
            MinWidth           = 160;

            if (E.S.OverlayX >= 0) Left = E.S.OverlayX;
            if (E.S.OverlayY >= 0) Top  = E.S.OverlayY;

            SourceInitialized += delegate {
                hwnd = new WindowInteropHelper(this).Handle;
                // Hide completely from Alt+Tab / Task Switcher / Windows app detection, and never steal game focus
                SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                hwndSource = HwndSource.FromHwnd(hwnd);
                if (hwndSource != null) hwndSource.AddHook(WndProc);
                fgProc = OnForegroundChanged;
                try { fgHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, fgProc, 0, 0, WINEVENT_OUTOFCONTEXT); } catch { }
                zGuardTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1000) };
                zGuardTimer.Tick += delegate { if (pinned && IsVisible) EnsureTopmostIfNeeded(); };
                ApplyPin(E.S.OverlayPin);
            };
            new WindowInteropHelper(this).EnsureHandle();

            var root = new StackPanel { Orientation = Orientation.Vertical };
            root.MouseLeftButtonDown += OnDrag;

            root.Children.Add(TelemetryRow(out cpuPct, out cpuTemp, "CPU"));
            root.Children.Add(TelemetryRow(out gpuPct, out gpuTemp, "GPU"));
            root.Children.Add(FanCompactRow());
            root.Children.Add(ToggleButton());

            fanPanel            = BuildFanPanel();
            fanPanel.Visibility = Visibility.Collapsed;
            root.Children.Add(fanPanel);

            Content = new Border {
                Background = BgBrush,
                BorderBrush = BorderBrush_,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2),
                Child = root
            };

            sensors.Updated += OnSensors;
            E.StateChanged  += OnEngineState;

            Closing += delegate { isClosing = true; };
            Closed  += delegate {
                sensors.Updated -= OnSensors;
                E.StateChanged  -= OnEngineState;
                if (zGuardTimer != null) zGuardTimer.Stop();
                if (fgHook != IntPtr.Zero) { try { UnhookWinEvent(fgHook); } catch { } fgHook = IntPtr.Zero; fgProc = null; }
                if (hwndSource != null) {
                    try { hwndSource.RemoveHook(WndProc); } catch { }
                    hwndSource = null;
                }
            };
            Deactivated += delegate { DoTopmost(); };

            SeedSliders();
            UpdateStatus();
        }

        // ====================================================================
        // Compact rows
        // ====================================================================

        StackPanel TelemetryRow(out TextBlock pct, out TextBlock temp, string label) {
            var row = HRow(8, 3, 8, 2);
            row.Children.Add(Lbl(label, 28));
            pct  = Mono("--%",   40, TextAlignment.Right);
            temp = Mono("--\u00b0C", 46, TextAlignment.Right);
            row.Children.Add(pct);
            row.Children.Add(temp);
            return row;
        }

        StackPanel FanCompactRow() {
            var row = HRow(8, 2, 8, 3);
            row.Children.Add(Lbl("FAN", 28));
            fanCompact = Mono("-- RPM", 100, TextAlignment.Right);
            row.Children.Add(fanCompact);
            return row;
        }

        // ====================================================================
        // Toggle button
        // ====================================================================

        UIElement ToggleButton() {
            fanToggleBtn = new Border {
                Background   = BtnBgBrush,
                CornerRadius = new CornerRadius(3),
                Padding      = new Thickness(0, 3, 0, 3),
                Margin       = new Thickness(8, 3, 8, 4),
                Cursor       = Cursors.Hand
            };
            fanToggleBtn.Child = Bold("FAN CONTROLS \u25be", 10, HorizontalAlignment.Center);
            fanToggleBtn.MouseLeftButtonDown += (s, e) => { e.Handled = true; TogglePanel(); };
            return fanToggleBtn;
        }

        void TogglePanel() {
            fanPanelOpen        = !fanPanelOpen;
            fanPanel.Visibility = fanPanelOpen ? Visibility.Visible : Visibility.Collapsed;
            ((TextBlock)fanToggleBtn.Child).Text = fanPanelOpen ? "FAN CONTROLS \u25b4" : "FAN CONTROLS \u25be";
        }

        // ====================================================================
        // Expanded fan panel
        // ====================================================================

        StackPanel BuildFanPanel() {
            int sMin = E.P.Curve.Floor;
            int sMax = E.IsEme ? Math.Max(57, E.P.Curve.Ceiling) : E.P.Curve.Ceiling;

            var panel = new StackPanel { Orientation = Orientation.Vertical };

            panel.Children.Add(new Border { Height = 1, Background = SepBrush, Margin = new Thickness(8, 0, 8, 6) });

            statusTxt = new TextBlock {
                Foreground    = DimBrush, FontFamily = UiFont, FontSize = 10,
                TextWrapping  = TextWrapping.Wrap,
                Margin        = new Thickness(8, 0, 8, 4),
                TextAlignment = TextAlignment.Center
            };
            panel.Children.Add(statusTxt);

            sl1 = new Slider(); targetTxt1 = new TextBlock(); actualTxt1 = new TextBlock();
            panel.Children.Add(FanSection("CPU FAN", sl1, targetTxt1, actualTxt1, sMin, sMax));

            sl2 = new Slider(); targetTxt2 = new TextBlock(); actualTxt2 = new TextBlock();
            gpu2Row = FanSection("GPU FAN", sl2, targetTxt2, actualTxt2, sMin, sMax);
            panel.Children.Add(gpu2Row);

            if (E.FanCount == 1) {
                gpu2Row.Visibility = Visibility.Collapsed;
                panel.Children.Add(new TextBlock {
                    Text = "Single-fan model \u2014 one slider controls it",
                    Foreground = DimBrush, FontFamily = UiFont, FontSize = 10,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 4)
                });
            }

            if (E.S.ModeIndex == 0) {
                panel.Children.Add(new TextBlock {
                    Text = "Eco: Auto fans stop below 50\u00b0C. Manual overrides this.",
                    Foreground = WarnBrush, FontFamily = UiFont, FontSize = 10,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 4)
                });
            }

            var btnRow = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 6)
            };
            btnRow.Children.Add(ActionBtn("Apply", OnApply));
            btnRow.Children.Add(ActionBtn("Auto",  OnAuto));
            btnRow.Children.Add(ActionBtn("5700 RPM", On5700Rpm));
            panel.Children.Add(btnRow);

            return panel;
        }

        StackPanel FanSection(string label, Slider sl, TextBlock target, TextBlock actual, int sMin, int sMax) {
            var section = new StackPanel { Margin = new Thickness(8, 0, 8, 6) };

            var header = HRow(0, 0, 0, 2);
            header.Children.Add(Bold(label, 10, HorizontalAlignment.Left));
            target.Foreground = Brushes.White; target.FontFamily = MonoFont;
            target.FontSize = 10; target.Margin = new Thickness(8, 0, 0, 0);
            header.Children.Add(target);
            section.Children.Add(header);

            sl.Minimum = sMin; sl.Maximum = sMax;
            sl.IsSnapToTickEnabled = true; sl.TickFrequency = 1;
            sl.Focusable = false; sl.Margin = new Thickness(0, 0, 0, 2);
            sl.Tag = AccentBrush;
            sl.Template = SliderTemplate(AccentBrush);
            sl.ValueChanged += OnSliderMoved;
            section.Children.Add(sl);

            var actualRow = HRow(0, 0, 0, 0);
            actualRow.Children.Add(Lbl("Actual:", 40));
            actual.Foreground = Brushes.White; actual.FontFamily = MonoFont;
            actual.FontSize = 10; actual.Text = "--"; actual.Margin = new Thickness(4, 0, 0, 0);
            actualRow.Children.Add(actual);
            section.Children.Add(actualRow);

            return section;
        }

        void OnSliderMoved(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (sl1 != null && targetTxt1 != null) targetTxt1.Text = LevelStr((int)sl1.Value);
            if (sl2 != null && targetTxt2 != null) targetTxt2.Text = LevelStr((int)sl2.Value);
        }

        // ====================================================================
        // Apply / Auto (run on Task.Run from ActionBtn)
        // ====================================================================

        void OnApply() {
            if (E.ReadOnly) { SetStatus("Read-only: board not recognised by Ohman", WarnBrush); return; }
            if (!E.CanSetFanLevels) { SetStatus("Fan level writes refused by this firmware", WarnBrush); return; }
            int maxLvl = E.IsEme ? Math.Max(57, E.P.Curve.Ceiling) : E.P.Curve.Ceiling;
            int v1 = (int)sl1.Value, v2 = (int)sl2.Value;
            int f1 = E.IsEme ? (v1 <= 0 ? 0 : Math.Max(E.P.Curve.Floor, Math.Min(maxLvl, v1))) : E.P.Curve.ClampOrOff(v1);
            int f2 = (E.FanCount == 1) ? f1 : (E.IsEme ? (v2 <= 0 ? 0 : Math.Max(E.P.Curve.Floor, Math.Min(maxLvl, v2))) : E.P.Curve.ClampOrOff(v2));
            System.Threading.Tasks.Task.Run(delegate { E.SetFan(FanMode.Manual, f1, f2, false); });
        }

        void OnAuto() {
            System.Threading.Tasks.Task.Run(delegate { E.SetFan(FanMode.Auto, E.S.Fan1, E.S.Fan2, false); });
        }

        void On5700Rpm() {
            if (E.ReadOnly) { SetStatus("Read-only: board not recognised by Ohman", WarnBrush); return; }
            if (!E.CanSetFanLevels) { SetStatus("Fan level writes refused by this firmware", WarnBrush); return; }
            if (!E.S.EmeConfirmed) {
                var res = MessageBox.Show(
                    "Unleashed Mode enables full manual fan control and maximum 5,700 RPM speeds, including on battery power.\n\n" +
                    "• High fan speeds on battery will increase power consumption and reduce battery runtime.\n" +
                    "• Fan noise will increase accordingly.\n" +
                    "• Hardware thermal safeguards remain active.\n\n" +
                    "Do you wish to enable 5700 RPM (Unleashed) mode?",
                    "Enable 5700 RPM Fans (Unleashed)?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;
                E.S.EmeConfirmed = true;
                E.S.Save();
            }
            System.Threading.Tasks.Task.Run(delegate {
                if (!E.IsEme) E.SetMode(3, false);
                E.SetFan(FanMode.Manual, 57, 57, false);
            });
        }

        // ====================================================================
        // Public: actual fan RPM update (called from main window hardware loop)
        // ====================================================================

        public void UpdateFans(int[] f) {
            Dispatcher.BeginInvoke((Action)delegate {
                if (f == null || f.Length == 0) return;
                bool dual = (E.FanCount != 1) && f.Length > 1;
                string s1 = FmtActual(f[0]);
                string s2 = dual ? FmtActual(f[1]) : null;
                fanCompact.Text = s2 != null ? s1 + " / " + s2 + " RPM" : s1 + " RPM";
                string unit = E.P.RpmPerLevel > 0 ? " RPM" : "% top";
                if (actualTxt1 != null) actualTxt1.Text = s1 + unit;
                if (actualTxt2 != null) actualTxt2.Text = (s2 ?? "--") + unit;
            });
        }

        string FmtActual(int level) {
            if (level < 0) return "--";
            return E.P.RpmPerLevel > 0 ? (level * E.P.RpmPerLevel).ToString() : E.Percent(level).TrimEnd('%');
        }

        // ====================================================================
        // Engine state change
        // ====================================================================

        void OnEngineState() {
            Dispatcher.BeginInvoke((Action)delegate {
                UpdateStatus();
                if ((E.GuardActive || E.Emergency71Active) && !fanPanelOpen) {
                    fanPanelOpen = true;
                    fanPanel.Visibility = Visibility.Visible;
                    ((TextBlock)fanToggleBtn.Child).Text = "FAN CONTROLS \u25b4";
                }
                SeedSliders();
                EnsureTopmostIfNeeded();
            });
        }

        // ====================================================================
        // Sensor snapshot
        // ====================================================================

        void OnSensors(SensorSnapshot s) {
            Dispatcher.BeginInvoke((Action)delegate {
                cpuPct.Text  = (!double.IsNaN(s.CpuLoad) && s.CpuLoad >= 0 ? ((int)Math.Round(s.CpuLoad)).ToString() : "--") + "%";
                cpuTemp.Text = (!double.IsNaN(s.CpuTemp) ? ((int)Math.Round(s.CpuTemp)).ToString() : "--") + "\u00b0C";
                gpuPct.Text  = (!double.IsNaN(s.GpuLoad) && s.GpuLoad >= 0 ? ((int)Math.Round(s.GpuLoad)).ToString() : "--") + "%";
                gpuTemp.Text = (!double.IsNaN(s.GpuTemp) ? ((int)Math.Round(s.GpuTemp)).ToString() : "--") + "\u00b0C";
            });
        }

        // ====================================================================
        // Status line
        // ====================================================================

        void UpdateStatus() {
            if (statusTxt == null) return;
            string txt; Brush col;
            if      (E.Emergency71Active)  { txt = "71\u00b0C EMERGENCY \u2014 max fan forced"; col = WarnBrush; }
            else if (E.GuardActive)        { txt = "Thermal guard \u2014 max fan forced";        col = WarnBrush; }
            else if (E.ReadOnly)           { txt = "Read-only \u2014 board not recognised";       col = WarnBrush; }
            else if (!E.CanSetFanLevels)   { txt = "Firmware refuses fan-level writes";           col = WarnBrush; }
            else switch (E.S.Fan) {
                case FanMode.Turbo6200: txt = "Heat Wipe (6200 RPM)"; col = AccentBrush; break;
                case FanMode.Manual: txt = (E.IsEme && (E.S.Fan1 >= 57 || E.S.Fan2 >= 57)) ? "5700 RPM Fans (Unleashed)" : "Manual \u2014 levels held by engine"; col = AccentBrush; break;
                case FanMode.Max:    txt = "Max fan";                             col = WarnBrush;   break;
                case FanMode.Custom: txt = "Custom curve";                        col = DimBrush;    break;
                default:             txt = "Auto (firmware curve)";               col = DimBrush;    break;
            }
            statusTxt.Text = txt; statusTxt.Foreground = col;
        }

        void SetStatus(string txt, Brush col) {
            Dispatcher.BeginInvoke((Action)delegate { if (statusTxt != null) { statusTxt.Text = txt; statusTxt.Foreground = col; } });
        }

        // ====================================================================
        // Slider seeding (no hardware write)
        // ====================================================================

        void SeedSliders() {
            if (sl1 == null) return;
            int floor = E.P.Curve.Floor, ceiling = E.IsEme ? Math.Max(57, E.P.Curve.Ceiling) : E.P.Curve.Ceiling;
            sl1.Minimum = floor; sl1.Maximum = ceiling;
            sl2.Minimum = floor; sl2.Maximum = ceiling;
            int f1 = Clamp(E.S.Fan1 > 0 ? E.S.Fan1 : floor, floor, ceiling);
            int f2 = Clamp(E.S.Fan2 > 0 ? E.S.Fan2 : floor, floor, ceiling);
            sl1.Value = f1; sl2.Value = f2;   // ValueChanged fires OnSliderMoved which updates labels
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        string LevelStr(int level) { return level < 0 ? "--" : E.Rpm(level); }

        // ====================================================================
        // Drag
        // ====================================================================

        void OnDrag(object sender, MouseButtonEventArgs e) {
            if (e.Handled) return;
            try { DragMove(); } catch { }
            E.S.OverlayX = (int)Left; E.S.OverlayY = (int)Top; E.S.Save();
            DoTopmost();
        }

        // ====================================================================
        // Always-on-top
        // ====================================================================

        public void ApplyPin(bool pin) {
            pinned = pin; base.Topmost = pin;
            if (zGuardTimer != null) { if (pin) zGuardTimer.Start(); else zGuardTimer.Stop(); }
            if (hwnd != IntPtr.Zero && !isClosing)
                SetWindowPos(hwnd, pin ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        void DoTopmost() {
            if (pinned && hwnd != IntPtr.Zero && !isClosing)
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr fgHwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime) {
            if (pinned && !isClosing && fgHwnd != hwnd) DoTopmost();
        }

        void EnsureTopmostIfNeeded() {
            if (!pinned || hwnd == IntPtr.Zero || isClosing) return;
            // Only call SetWindowPos when a visible topmost window (e.g. borderless/fullscreen game) jumped above us in Z-order
            if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0) { DoTopmost(); return; }
            IntPtr prev = GetWindow(hwnd, GW_HWNDPREV);
            while (prev != IntPtr.Zero) {
                if ((GetWindowLong(prev, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0) break;
                if (IsWindowVisible(prev)) { DoTopmost(); break; }
                prev = GetWindow(prev, GW_HWNDPREV);
            }
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
            if (msg == WM_MOUSEACTIVATE) {
                handled = true;
                return (IntPtr)MA_NOACTIVATE;
            }
            if (msg == WM_WINDOWPOSCHANGING && pinned && !isClosing) {
                try {
                    var wp = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS));
                    if (wp.hwndInsertAfter != HWND_TOPMOST) {
                        wp.hwndInsertAfter = HWND_TOPMOST;
                        wp.flags &= ~SWP_NOZORDER;
                        Marshal.StructureToPtr(wp, lParam, false);
                    }
                } catch { }
            }
            return IntPtr.Zero;
        }

        // ====================================================================
        // Widget helpers
        // ====================================================================

        static StackPanel HRow(double l, double t, double r, double b) {
            return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(l, t, r, b) };
        }
        static TextBlock Lbl(string text, double width) {
            return new TextBlock { Text = text, Foreground = DimBrush, Width = width, FontFamily = UiFont, FontSize = 12 };
        }
        static TextBlock Bold(string text, double size, HorizontalAlignment ha) {
            return new TextBlock { Text = text, Foreground = Brushes.White, FontFamily = UiFont, FontSize = size, FontWeight = FontWeights.Bold, HorizontalAlignment = ha };
        }
        static TextBlock Mono(string text, double width, TextAlignment align) {
            return new TextBlock { Text = text, Foreground = Brushes.White, FontFamily = MonoFont, FontSize = 13, Width = width, TextAlignment = align };
        }
        Border ActionBtn(string label, Action action) {
            var bdr = new Border {
                Background = BtnBgBrush, CornerRadius = new CornerRadius(3),
                Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(4, 0, 4, 0), Cursor = Cursors.Hand
            };
            bdr.Child = new TextBlock { Text = label, Foreground = Brushes.White, FontFamily = UiFont, FontSize = 10, FontWeight = FontWeights.Bold };
            bdr.MouseLeftButtonDown += (s, e) => { e.Handled = true; action(); };
            return bdr;
        }

        // ====================================================================
        // Slider template
        // ====================================================================

        static ControlTemplate SliderTemplate(Brush accent) {
            string xaml =
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"Slider\">" +
                "  <Grid Height=\"16\" Background=\"Transparent\">" +
                "    <Border Height=\"2\" Background=\"#50FFFFFF\" VerticalAlignment=\"Center\" Margin=\"6,0\"/>" +
                "    <Track x:Name=\"PART_Track\" VerticalAlignment=\"Center\">" +
                "      <Track.DecreaseRepeatButton>" +
                "        <RepeatButton Focusable=\"False\" IsTabStop=\"False\">" +
                "          <RepeatButton.Template><ControlTemplate TargetType=\"RepeatButton\"><Grid Background=\"Transparent\"><Border Height=\"2\" Background=\"{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}\" VerticalAlignment=\"Center\" Margin=\"6,0,0,0\"/></Grid></ControlTemplate></RepeatButton.Template>" +
                "        </RepeatButton>" +
                "      </Track.DecreaseRepeatButton>" +
                "      <Track.IncreaseRepeatButton>" +
                "        <RepeatButton Focusable=\"False\" IsTabStop=\"False\">" +
                "          <RepeatButton.Template><ControlTemplate TargetType=\"RepeatButton\"><Grid Background=\"Transparent\"/></ControlTemplate></RepeatButton.Template>" +
                "        </RepeatButton>" +
                "      </Track.IncreaseRepeatButton>" +
                "      <Track.Thumb>" +
                "        <Thumb Width=\"12\" Height=\"12\" Focusable=\"False\" Cursor=\"Hand\">" +
                "          <Thumb.Template><ControlTemplate TargetType=\"Thumb\"><Ellipse Fill=\"{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}\"/></ControlTemplate></Thumb.Template>" +
                "        </Thumb>" +
                "      </Track.Thumb>" +
                "    </Track>" +
                "  </Grid>" +
                "</ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }
    }
}
