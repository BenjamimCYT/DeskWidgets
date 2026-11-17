using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeskWidgets.Helpers;
using DeskWidgets.Services;

namespace DeskWidgets.Widgets
{
    public partial class WeatherWidget : WidgetBase
    {
        public override string Id => "weather";
        public override string DisplayName => "Clima";
        public override string Glyph => "";
        public override Size BaseSize => new Size(190, 100);

        // Clima a cada 30 minutos, fora da interface
        private readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(30) };
        private WeatherNow _last;
        private DateTime _lastFetch = DateTime.MinValue;
        private bool _busy;

        public WeatherWidget()
        {
            InitializeComponent();
            _timer.Tick += (s, e) => Refresh();
        }

        public override Brush TileBackground(Brush themeBrush) =>
            S.WeatherTransparentTile ? ColorUtil.AlmostTransparent() : themeBrush;

        public override bool ShowTileBorder => !S.WeatherTransparentTile;

        public override void ApplySettings()
        {
            if (_last != null) Show(_last);
            else WeatherCity.Text = S.City;
        }

        public override void OnShown()
        {
            _timer.Start();
            if (DateTime.Now - _lastFetch > TimeSpan.FromMinutes(25)) Refresh();
        }

        public override void OnHidden() => _timer.Stop();

        public override async void Refresh()
        {
            if (_busy || !HostVisible) return;
            _busy = true;
            Busy.Visibility = Visibility.Visible;
            Busy.IsActive = true;
            try
            {
                var w = await WeatherService.GetCurrentAsync(S.Latitude, S.Longitude, S.TemperatureUnit);
                _last = w;
                _lastFetch = DateTime.Now;
                Show(w);
                _timer.Interval = TimeSpan.FromMinutes(30);
            }
            catch (Exception ex)
            {
                SettingsService.Log(ex);
                WeatherCity.Text = S.City + " (sem conexão)";
                ToolTip = "Não foi possível atualizar o clima. Nova tentativa em 5 minutos.";
                _timer.Interval = TimeSpan.FromMinutes(5);
            }
            finally
            {
                _busy = false;
                Busy.IsActive = false;
                Busy.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Esquece a última leitura (cidade ou unidade mudou) e busca de novo.</summary>
        public void Reload()
        {
            _last = null;
            _lastFetch = DateTime.MinValue;
            WeatherTemp.Text = "--";
            WeatherCity.Text = S.City;
            Refresh();
        }

        private void Show(WeatherNow w)
        {
            WeatherIcon.Source = WeatherIcons.Get(WeatherIcons.KindFromCode(w.Code), w.IsDay);
            WeatherTemp.Inlines.Clear();
            WeatherTemp.Inlines.Add(new Run(((int)Math.Round(w.Temperature)).ToString()));
            WeatherTemp.Inlines.Add(new Run(S.TemperatureUnit == "fahrenheit" ? "°F" : "°C") { FontSize = 17 });
            WeatherDesc.Text = WeatherService.Describe(w.Code);
            WeatherCity.Text = S.City;
            ToolTip = WeatherService.Describe(w.Code) + " em " + S.City + " (clique para atualizar)";
        }

        private void Weather_Click(object sender, MouseButtonEventArgs e)
        {
            if (WasDragged) return;
            Refresh();
        }
    }
}
