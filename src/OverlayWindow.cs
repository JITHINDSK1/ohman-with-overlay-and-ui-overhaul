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

        // -- Win32 topmost / dragging ----------------------------------------
        bool     pinned, isClosing;
        IntPtr   hwnd = IntPtr.Zero;
        HwndSource hwndSource;

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        static readonly IntPtr HWND_TOPMOST   = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        const uint SWP_NOSIZE     = 0x0001;
        const uint SWP_NOMOVE     = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_NOZORDER   = 0x0004;
        const int  WM_WINDOWPOSCHANGING = 0x0046;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct WINDOWPOS {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        // -- palette ---------------------------------------------------------
        static readonly Brush BgBrush     = Freeze(new SolidColorBrush(Color.FromArgb(235, 11, 13, 19)));
        static readonly Brush AccentBrush = Freeze(new SolidColorBrush(Color.FromRgb(59, 130, 246)));
        static readonly Brush DimBrush    = Freeze(new SolidColorBrush(Color.FromRgb(148, 163, 184)));
        static readonly Brush WarnBrush   = Freeze(new SolidColorBrush(Color.FromRgb(245, 158, 11)));
        static readonly Brush SepBrush    = Freeze(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)));
        static readonly Brush BtnBgBrush  = Freeze(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)));
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
            ShowActivated      = false;
            ShowInTaskbar      = false;
            ResizeMode         = ResizeMode.NoResize;
            SizeToContent      = SizeToContent.WidthAndHeight;
            Background         = BgBrush;
            MinWidth           = 160;

            if (E.S.OverlayX >= 0) Left = E.S.OverlayX;
            if (E.S.OverlayY >= 0) Top  = E.S.OverlayY;

            SourceInitialized += delegate {
                hwnd = new WindowInteropHelper(this).Handle;
                hwndSource = HwndSource.FromHwnd(hwnd);
                if (hwndSource != null) hwndSource.AddHook(WndProc);
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

            Content = root;

            sensors.Updated += OnSensors;
            E.StateChanged  += OnEngineState;

            Closing += delegate { isClosing = true; };
            Closed  += delegate {
                sensors.Updated -= OnSensors;
                E.StateChanged  -= OnEngineState;
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
            // Read slider values on the UI thread, then call engine from here (any thread is fine)
            int f1 = 0, f2 = 0;
            Dispatcher.Invoke(delegate {
                int maxLvl = E.IsEme ? Math.Max(57, E.P.Curve.Ceiling) : E.P.Curve.Ceiling;
                int v1 = (int)sl1.Value, v2 = (int)sl2.Value;
                f1 = E.IsEme ? (v1 <= 0 ? 0 : Math.Max(E.P.Curve.Floor, Math.Min(maxLvl, v1))) : E.P.Curve.ClampOrOff(v1);
                f2 = (E.FanCount == 1) ? f1 : (E.IsEme ? (v2 <= 0 ? 0 : Math.Max(E.P.Curve.Floor, Math.Min(maxLvl, v2))) : E.P.Curve.ClampOrOff(v2));
            });
            E.SetFan(FanMode.Manual, f1, f2, false);
        }

        void OnAuto() {
            E.SetFan(FanMode.Auto, E.S.Fan1, E.S.Fan2, false);
        }

        void On5700Rpm() {
            if (E.ReadOnly) { SetStatus("Read-only: board not recognised by Ohman", WarnBrush); return; }
            if (!E.CanSetFanLevels) { SetStatus("Fan level writes refused by this firmware", WarnBrush); return; }
            if (!E.S.EmeConfirmed) {
                bool ok = false;
                Dispatcher.Invoke(delegate {
                    var res = MessageBox.Show(
                        "Extreme Mode (EME) enables full manual fan control and maximum 5,700 RPM speeds, including on battery power.\n\n" +
                        "• High fan speeds on battery will increase power consumption and reduce battery runtime.\n" +
                        "• Fan noise will increase accordingly.\n" +
                        "• Hardware thermal safeguards remain active.\n\n" +
                        "Do you wish to enable 5700 RPM (EME) mode?",
                        "Enable 5700 RPM Fans (EME)?",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (res == MessageBoxResult.Yes) { E.S.EmeConfirmed = true; E.S.Save(); ok = true; }
                });
                if (!ok) return;
            }
            if (!E.IsEme) E.SetMode(3, false);
            E.SetFan(FanMode.Manual, 57, 57, false);
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
                DoTopmost();
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
                DoTopmost();
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
                case FanMode.Manual: txt = (E.IsEme && (E.S.Fan1 >= 57 || E.S.Fan2 >= 57)) ? "5700 RPM Fans (EME)" : "Manual \u2014 levels held by engine"; col = AccentBrush; break;
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
            if (hwnd != IntPtr.Zero && !isClosing)
                SetWindowPos(hwnd, pin ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        void DoTopmost() {
            if (pinned && hwnd != IntPtr.Zero && !isClosing)
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
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
            bdr.MouseLeftButtonDown += (s, e) => { e.Handled = true; System.Threading.Tasks.Task.Run(action); };
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
