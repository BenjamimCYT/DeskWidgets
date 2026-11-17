using System;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DeskWidgets.Widgets
{
    public partial class ClockWidget : WidgetBase
    {
        public override string Id => "clock";
        public override string DisplayName => "Relógio";
        public override string Glyph => "";
        public override Size BaseSize => new Size(300, 190);
        public override Size DefaultSize => new Size(320, 190);

        // Acorda 1x por minuto (ou 1x por segundo se os segundos estiverem visíveis), exatamente na virada
        private readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Normal);
        private CultureInfo _culture = CultureInfo.CurrentCulture;

        public ClockWidget()
        {
            InitializeComponent();
            _timer.Tick += (s, e) => { _timer.Stop(); UpdateClock(); ScheduleNextTick(); };
            SystemEvents.TimeChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() => { if (HostVisible) { UpdateClock(); ScheduleNextTick(); } }));
            SystemEvents.PowerModeChanged += (s, e) =>
            {
                if (e.Mode == PowerModes.Resume)
                    Dispatcher.BeginInvoke(new Action(() => { if (HostVisible) { UpdateClock(); ScheduleNextTick(); } }));
            };
        }

        internal static CultureInfo SafeCulture(string name)
        {
            try { return CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(name) ? "pt-BR" : name); }
            catch { return CultureInfo.CurrentCulture; }
        }

        internal static string Cap(string s, CultureInfo c) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], c) + s.Substring(1);

        public override void ApplySettings()
        {
            _culture = SafeCulture(S.DateCulture);
            FontFamily font;
            try { font = new FontFamily(string.IsNullOrWhiteSpace(S.ClockFont) ? "Times New Roman" : S.ClockFont); }
            catch { font = new FontFamily("Times New Roman"); }
            ClockText.FontFamily = font;
            ClockText.FontWeight = S.ClockBold ? FontWeights.Bold : FontWeights.Normal;
            DateShort.FontFamily = font;
            DateLong.FontFamily = font;
            var dateVis = S.ClockShowDate ? Visibility.Visible : Visibility.Collapsed;
            DateShort.Visibility = dateVis;
            DateLong.Visibility = dateVis;
            if (HostVisible) { UpdateClock(); ScheduleNextTick(); }
        }

        public override void OnShown() { UpdateClock(); ScheduleNextTick(); }
        public override void OnHidden() => _timer.Stop();

        private void ScheduleNextTick()
        {
            var now = DateTime.Now;
            TimeSpan wait = S.ShowSeconds
                ? TimeSpan.FromMilliseconds(1000 - now.Millisecond + 5)
                : TimeSpan.FromMilliseconds((60 - now.Second) * 1000 - now.Millisecond + 20);
            _timer.Stop();
            _timer.Interval = wait;
            _timer.Start();
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            string fmt = S.Clock24h ? "HH:mm" : "h:mm";
            if (S.ShowSeconds) fmt += ":ss";

            ClockText.Inlines.Clear();
            ClockText.Inlines.Add(new Run(now.ToString(fmt, _culture)));
            if (!S.Clock24h)
            {
                string ampm = now.ToString("tt", _culture);
                if (!string.IsNullOrWhiteSpace(ampm))
                    ClockText.Inlines.Add(new Run(" " + ampm) { FontSize = ClockText.FontSize * 0.28 });
            }

            var dtf = _culture.DateTimeFormat;
            string md = dtf.ShortDatePattern.TrimStart().StartsWith("M") ? "MM/dd" : "dd/MM";
            DateShort.Text = now.ToString(md, _culture);
            DateLong.Text = Cap(dtf.GetDayName(now.DayOfWeek), _culture) + ", " + dtf.GetMonthName(now.Month);
        }
    }
}
