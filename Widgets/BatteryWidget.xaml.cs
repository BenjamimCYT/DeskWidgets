using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeskWidgets.Helpers;
using DeskWidgets.Models;
using DeskWidgets.Services;

namespace DeskWidgets.Widgets
{
    public partial class BatteryWidget : WidgetBase
    {
        public override string Id => "battery";
        public override string DisplayName => "Bateria";
        public override string Glyph => "";
        public override Size BaseSize => new Size(200, 110);
        public override Size DefaultSize => new Size(210, 110);
        public override Size MinSize => new Size(80, 70);

        private readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Background);
        private readonly DispatcherTimer _kick = new DispatcherTimer(DispatcherPriority.Background);
        private List<DeviceBattery> _devices = new List<DeviceBattery>();
        private bool _busy, _initialized;

        public IReadOnlyList<DeviceBattery> LastDevices => _devices;
        public event Action DevicesUpdated;

        public BatteryWidget()
        {
            InitializeComponent();
            _timer.Tick += (s, e) => Refresh();
            _kick.Tick += (s, e) => { _kick.Stop(); Refresh(); };
        }

        public override void ApplySettings()
        {
            _timer.Interval = TimeSpan.FromMinutes(Math.Max(1, S.BatteryRefreshMinutes));
            Render();
        }

        public override void OnShown()
        {
            if (!_initialized)
            {
                _initialized = true;
                BatteryService.Init(() => Dispatcher.BeginInvoke(new Action(Kick)));
                // Mouse 2.4 GHz mandou um pacote de bateria: atualiza na hora, sem esperar o timer
                VendorHidBattery.LevelReceived += (key, level) => Dispatcher.BeginInvoke(new Action(() => OnVendorLevel(key, level)));
                _kick.Interval = TimeSpan.FromSeconds(3);
                _kick.Start();
            }
            else Refresh();
            _timer.Start();
        }

        public override void OnHidden() => _timer.Stop();

        private void Kick()
        {
            _kick.Interval = TimeSpan.FromSeconds(2);
            _kick.Stop();
            _kick.Start();
        }

        public override async void Refresh()
        {
            if (_busy) return;
            _busy = true;
            Scan.IsActive = true;
            EmptyText.Text = "Procurando…";
            try
            {
                var persisted = new Dictionary<string, int>(S.LastVendorBattery);
                var list = await Task.Run(() => BatteryService.GetAllAsync(persisted));
                _devices = list;
                Render();
                DevicesUpdated?.Invoke();
            }
            catch (Exception ex) { SettingsService.Log(ex); }
            finally
            {
                _busy = false;
                Scan.IsActive = false;
                EmptyText.Text = "Nenhum dispositivo";
            }
        }

        private void OnVendorLevel(string key, int level)
        {
            S.LastVendorBattery[key] = level;
            SettingsService.SaveSoon();
            var dev = _devices.FirstOrDefault(d => d.Key == key);
            if (dev == null) { Refresh(); return; }
            if (dev.Level == level) return;
            dev.Level = level;
            Render();
            DevicesUpdated?.Invoke();
        }

        public void Render()
        {
            DeviceGrid.Children.Clear();
            var visible = _devices.Where(d => !S.HiddenDevices.Contains(d.Key)).ToList();
            Empty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DeviceGrid.Columns = Math.Max(1, Math.Min(visible.Count, visible.Count <= 4 ? 4 : 3));
            if (ActualWidth > 0 && ActualHeight > ActualWidth * 0.9 && visible.Count > 1) DeviceGrid.Columns = 2;
            foreach (var d in visible) DeviceGrid.Children.Add(BuildItem(d));
        }

        private UIElement BuildItem(DeviceBattery d)
        {
            const double size = 38, stroke = 3.6;
            double r = (size - stroke) / 2;
            var ring = new Grid { Width = size, Height = size };
            var track = new Ellipse { StrokeThickness = stroke };
            track.SetResourceReference(Shape.StrokeProperty, "SubtleBrush");
            ring.Children.Add(track);
            ring.Children.Add(new Path
            {
                Data = ShapeUtil.Arc(new Point(size / 2, size / 2), r, d.Level / 100.0),
                Stroke = ShapeUtil.LevelBrush(d.Level),
                StrokeThickness = stroke,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
            ring.Children.Add(new TextBlock
            {
                Text = d.Glyph,
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            bool waiting = d.Level < 0;
            var stack = new StackPanel
            {
                Margin = new Thickness(7, 4, 7, 4),
                ToolTip = waiting
                    ? d.Name + ": aguardando o mouse informar a bateria (mexa nele ou espere alguns minutos)"
                    : d.Name + ": " + d.Level + "%"
            };
            stack.Children.Add(ring);
            stack.Children.Add(new TextBlock
            {
                Text = waiting ? "…" : d.Level + "%",
                FontSize = 11,
                Opacity = 0.85,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });
            return stack;
        }
    }
}
