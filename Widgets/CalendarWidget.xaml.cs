using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DeskWidgets.Widgets
{
    public partial class CalendarWidget : WidgetBase
    {
        public override string Id => "calendar";
        public override string DisplayName => "Calendário";
        public override string Glyph => "";
        public override Size BaseSize => new Size(300, 270);
        public override Size DefaultSize => new Size(320, 270);

        private CultureInfo _culture = CultureInfo.CurrentCulture;
        private DateTime _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        // Acorda só na virada do dia
        private readonly DispatcherTimer _midnight = new DispatcherTimer(DispatcherPriority.Background);

        public CalendarWidget()
        {
            InitializeComponent();
            _midnight.Tick += (s, e) =>
            {
                _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                Build();
                ScheduleMidnight();
            };
        }

        public override void ApplySettings()
        {
            _culture = ClockWidget.SafeCulture(S.DateCulture);
            BuildHeader();
            Build();
        }

        public override void OnShown() { Build(); ScheduleMidnight(); }
        public override void OnHidden() => _midnight.Stop();

        private void ScheduleMidnight()
        {
            _midnight.Stop();
            _midnight.Interval = DateTime.Today.AddDays(1) - DateTime.Now + TimeSpan.FromSeconds(2);
            _midnight.Start();
        }

        private void BuildHeader()
        {
            CalHeader.Children.Clear();
            var dtf = _culture.DateTimeFormat;
            int first = (int)dtf.FirstDayOfWeek;
            for (int i = 0; i < 7; i++)
            {
                var dow = (DayOfWeek)((first + i) % 7);
                string name = dtf.AbbreviatedDayNames[(int)dow].TrimEnd('.');
                if (name.Length > 3) name = name.Substring(0, 3);
                var tb = new TextBlock
                {
                    Text = ClockWidget.Cap(name, _culture),
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                CalHeader.Children.Add(tb);
            }
        }

        private void Build()
        {
            CalDays.Children.Clear();
            var today = DateTime.Today;
            var m = _month;
            CalTitle.Text = ClockWidget.Cap(m.ToString("MMMM yyyy", _culture), _culture);

            int first = (int)_culture.DateTimeFormat.FirstDayOfWeek;
            int offset = ((int)m.DayOfWeek - first + 7) % 7;
            int days = DateTime.DaysInMonth(m.Year, m.Month);
            int rows = (int)Math.Ceiling((offset + days) / 7.0);
            CalDays.Rows = rows;

            for (int i = 0; i < rows * 7; i++)
            {
                int d = i - offset + 1;
                var cell = new Grid { MinHeight = 26 };
                if (d >= 1 && d <= days)
                {
                    var date = new DateTime(m.Year, m.Month, d);
                    bool isToday = date == today;
                    bool weekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
                    var tb = new TextBlock
                    {
                        Text = d.ToString(CultureInfo.InvariantCulture),
                        FontSize = 14,
                        FontWeight = FontWeights.Normal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Opacity = date < today ? 0.55 : weekend ? 0.8 : 1
                    };
                    if (isToday)
                    {
                        var dot = new Ellipse { Width = 27, Height = 27 };
                        dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
                        cell.Children.Add(dot);
                        tb.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
                        tb.FontWeight = FontWeights.SemiBold;
                        tb.Opacity = 1;
                    }
                    cell.Children.Add(tb);
                }
                CalDays.Children.Add(cell);
            }
        }

        private void Prev_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(-1); Build(); }
        private void Next_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(1); Build(); }

        private void CalTitle_Click(object sender, MouseButtonEventArgs e)
        {
            if (WasDragged) return;
            _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            Build();
        }
    }
}
