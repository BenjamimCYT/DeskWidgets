using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeskWidgets.Helpers;
using DeskWidgets.Services;

namespace DeskWidgets.Widgets
{
    public partial class HardwareWidget : WidgetBase
    {
        public override string Id => "hardware";
        public override string DisplayName => "Componentes";
        public override string Glyph => "";
        public override Size BaseSize => new Size(210, 170);
        public override Size MinSize => new Size(150, 90);

        private readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Background);
        private bool _busy;

        public HardwareSnapshot Last { get; private set; }
        public event Action Updated;

        public HardwareWidget()
        {
            InitializeComponent();
            _timer.Tick += (s, e) => Refresh();
        }

        public override void ApplySettings()
        {
            _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(S.HardwareRefreshSeconds, 2, 120));
            if (Last != null) Render(Last);
        }

        public override void OnShown() { _timer.Start(); Refresh(); }
        public override void OnHidden() { _timer.Stop(); Live.IsActive = false; }

        public override async void Refresh()
        {
            if (_busy || !HostVisible) return;
            _busy = true;
            try
            {
                var snap = await Task.Run(HardwareService.Read);
                Last = snap;
                Render(snap);
                Updated?.Invoke();
            }
            catch (Exception ex) { SettingsService.Log(ex); }
            finally { _busy = false; }
        }

        private string Temp(double c)
        {
            if (S.TemperatureUnit == "fahrenheit") return $"{c * 9 / 5 + 32:0}°F";
            return $"{c:0}°C";
        }

        private void Render(HardwareSnapshot s)
        {
            Rows.Children.Clear();
            double warn = Math.Clamp(S.HardwareWarnTemp, 40, 110);
            bool hot = false;

            if (S.HwShowCpuTemp)
            {
                if (s.CpuTemp is double t)
                {
                    hot |= t >= warn;
                    AddRow("", "CPU" + (s.CpuTempSource == "aprox." ? " (aprox.)" : ""), Temp(t), t / 100.0, ShapeUtil.TempBrush(t, warn));
                }
                else AddRow("", "CPU", "—", 0, null, "Abra o LibreHardwareMonitor para ler a temperatura da CPU");
            }
            if (S.HwShowGpuTemp)
            {
                if (s.GpuTemp is double g)
                {
                    hot |= g >= warn;
                    string extra = s.GpuLoad is double gl ? $"  ·  {gl:0}%" : "";
                    AddRow("", "GPU", Temp(g) + extra, g / 100.0, ShapeUtil.TempBrush(g, warn), s.GpuName);
                }
                else AddRow("", "GPU", "—", 0, null, "GPU NVIDIA é lida pelo driver; para AMD/Intel, abra o LibreHardwareMonitor");
            }
            if (S.HwShowDiskTemp && s.DiskTemp is double d)
                AddRow("", "SSD", Temp(d), d / 100.0, ShapeUtil.TempBrush(d, Math.Min(warn, 70)));
            if (S.HwShowCpuLoad)
                AddRow("", "Uso da CPU", $"{s.CpuLoad:0}%", s.CpuLoad / 100.0, null);
            if (S.HwShowRam)
                AddRow("", "Memória", $"{s.RamUsedGb:0.0}/{s.RamTotalGb:0} GB", s.RamPercent / 100.0, null);

            if (Rows.Children.Count == 0)
                Rows.Children.Add(new TextBlock { Text = "Escolha o que mostrar nas configurações.", FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });

            Live.IsActive = hot && HostVisible;
            Live.Brush = hot ? ColorUtil.Solid(Color.FromRgb(0xFF, 0x5A, 0x5F)) : (Brush)FindResource("AccentBrush");
            ToolTip = "Fontes: " + HardwareService.LastSources;
        }

        private void AddRow(string glyph, string label, string value, double frac, Brush bar, string tip = null)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 7), ToolTip = tip };
            var top = new DockPanel();
            var v = new TextBlock { Text = value, FontSize = 12, FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(v, Dock.Right);
            top.Children.Add(v);
            var icon = new TextBlock
            {
                Text = glyph, FontSize = 11, Margin = new Thickness(0, 1, 7, 0), Opacity = 0.8,
                FontFamily = (FontFamily)FindResource("IconFont"), VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(icon, Dock.Left);
            top.Children.Add(icon);
            top.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.85, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(top);

            var track = new Grid { Height = 4, Margin = new Thickness(0, 4, 0, 0) };
            var bg = new Border { CornerRadius = new CornerRadius(2) };
            bg.SetResourceReference(Border.BackgroundProperty, "SubtleBrush");
            track.Children.Add(bg);
            var fill = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
            if (bar != null) fill.Background = bar; else fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            frac = Math.Clamp(frac, 0, 1);
            track.SizeChanged += (s, e) => fill.Width = track.ActualWidth * frac;
            track.Children.Add(fill);
            row.Children.Add(track);

            Rows.Children.Add(row);
        }
    }
}
