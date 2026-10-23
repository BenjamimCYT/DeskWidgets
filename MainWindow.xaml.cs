using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using DeskWidgets.Helpers;
using DeskWidgets.Models;
using DeskWidgets.Services;

namespace DeskWidgets
{
    public partial class MainWindow : Window
    {
        private static AppSettings S => SettingsService.Current;

        // Timers "preguiçosos": o relógio acorda 1x por minuto (ou 1x por segundo só se os segundos estiverem visíveis)
        private readonly DispatcherTimer _clockTimer = new DispatcherTimer(DispatcherPriority.Normal);
        private readonly DispatcherTimer _weatherTimer = new DispatcherTimer(DispatcherPriority.Background);
        private readonly DispatcherTimer _batteryTimer = new DispatcherTimer(DispatcherPriority.Background);
        private readonly DispatcherTimer _batteryKick = new DispatcherTimer(DispatcherPriority.Background);

        private CultureInfo _culture = CultureInfo.CurrentCulture;
        private DateTime _calMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime _lastDay = DateTime.Today;
        private Brush _tileBrush;
        private IntPtr _hwnd;
        private bool _allowFront;

        private Point _downPos;
        private bool _maybeDrag;
        private bool _dragged;

        private WeatherNow _lastWeather;
        private bool _weatherBusy;
        private bool _batteryBusy;
        private List<DeviceBattery> _devices = new List<DeviceBattery>();

        public IReadOnlyList<DeviceBattery> LastDevices => _devices;
        public event Action DevicesUpdated;

        public MainWindow()
        {
            InitializeComponent();

            NotesPreview.ItemsSource = SettingsService.Notes;
            NotesList.ItemsSource = SettingsService.Notes;
            SettingsService.NotesChanged += UpdateNotesSummary;

            _clockTimer.Tick += ClockTick;
            _weatherTimer.Interval = TimeSpan.FromMinutes(30);
            _weatherTimer.Tick += (s, e) => RefreshWeather();
            _batteryTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, S.BatteryRefreshMinutes));
            _batteryTimer.Tick += (s, e) => RefreshBattery();
            _batteryKick.Interval = TimeSpan.FromSeconds(2);
            _batteryKick.Tick += (s, e) => { _batteryKick.Stop(); RefreshBattery(); };

            PlaceWindow();
            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Deactivated += OnDeactivated;
            LocationChanged += (s, e) =>
            {
                if (!IsLoaded) return;
                S.Left = Left;
                S.Top = Top;
                SettingsService.SaveSoon();
            };
        }

        // ================== Janela ==================

        private void PlaceWindow()
        {
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                  SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (!screen.Contains(new Point(S.Left + 40, S.Top + 40)))
            {
                var wa = SystemParameters.WorkArea;
                S.Left = wa.Right - 420;
                S.Top = wa.Top + 40;
            }
            Left = S.Left;
            Top = S.Top;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            // Some da barra de tarefas e do Alt+Tab
            long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            ex = (ex | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));

            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        }

        /// <summary>No modo "fixo na área de trabalho" toda tentativa de vir para frente é mandada para o fundo.</summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_WINDOWPOSCHANGING && S.ZMode == "Desktop" && !_allowFront)
            {
                var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
                pos.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
                pos.flags &= ~NativeMethods.SWP_NOZORDER;
                Marshal.StructureToPtr(pos, lParam, false);
            }
            return IntPtr.Zero;
        }

        private void ApplyZOrder()
        {
            Topmost = S.ZMode == "Topmost";
            if (S.ZMode == "Desktop" && _hwnd != IntPtr.Zero && !_allowFront)
            {
                NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        }

        /// <summary>Traz o widget de volta (ex.: depois de Win+D) pelo ícone da bandeja.</summary>
        public void BringBack()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            _allowFront = true; // fica na frente até você clicar em outra janela
            Activate();
        }

        private void OnDeactivated(object sender, EventArgs e)
        {
            if (_allowFront && NotesOverlay.Visibility != Visibility.Visible)
            {
                _allowFront = false;
                ApplyZOrder();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplySettings();
            UpdateNotesSummary();

            _weatherTimer.Start();
            _batteryTimer.Start();
            RefreshWeather();
            _batteryKick.Interval = TimeSpan.FromSeconds(3);
            _batteryKick.Start();

            BatteryService.Init(() => Dispatcher.BeginInvoke(new Action(() =>
            {
                _batteryKick.Interval = TimeSpan.FromSeconds(2);
                _batteryKick.Stop();
                _batteryKick.Start();
            })));

            SystemEvents.TimeChanged += (s, a) => Dispatcher.BeginInvoke(new Action(() => { UpdateClock(); ScheduleNextTick(); }));
            SystemEvents.PowerModeChanged += (s, a) =>
            {
                if (a.Mode != PowerModes.Resume) return;
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    UpdateClock();
                    ScheduleNextTick();
                    await Task.Delay(8000); // dá tempo da rede/Bluetooth voltarem
                    RefreshAll();
                }));
            };

            var trim = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(10) };
            trim.Tick += (s, a) => { trim.Stop(); ResourceSaver.Trim(); };
            trim.Start();
        }

        // ================== Aparência ==================

        public void ApplySettings()
        {
            _culture = SafeCulture(S.DateCulture);

            double scale = Math.Clamp(S.Scale, 0.5, 2.5);
            RootScale.ScaleX = scale;
            RootScale.ScaleY = scale;

            var text = ColorUtil.Parse(S.TextColor, Colors.White);
            var accent = ColorUtil.Parse(S.Accent, Colors.White);
            var textBrush = ColorUtil.Solid(text);
            Resources["TextBrush"] = textBrush;
            Resources["AccentBrush"] = ColorUtil.Solid(accent);
            Resources["AccentTextBrush"] = ColorUtil.ContrastFor(accent);
            TextElement.SetForeground(Root, textBrush);

            _tileBrush = ColorUtil.Gradient(S);
            var radius = new CornerRadius(Math.Clamp(S.CornerRadius, 0, 40));
            foreach (var tile in new[] { TileNotes, TileBattery, TileImage, TileWeather, TileClock, TileCalendar, NotesOverlay })
            {
                tile.CornerRadius = radius;
                tile.Background = _tileBrush;
            }
            // A checklist aberta fica quase opaca para o texto ser legível sobre qualquer fundo
            NotesOverlay.Background = ColorUtil.Gradient(S, Math.Max(S.TileOpacity, 0.94));
            if (S.WeatherTransparentTile) TileWeather.Background = ColorUtil.AlmostTransparent();
            ApplyImage();

            var font = new FontFamily(string.IsNullOrWhiteSpace(S.ClockFont) ? "Times New Roman" : S.ClockFont);
            ClockText.FontFamily = font;
            ClockText.FontSize = Math.Clamp(S.ClockSize, 16, 200);
            ClockText.FontWeight = S.ClockBold ? FontWeights.Bold : FontWeights.Normal;
            DateShort.FontFamily = font;
            DateLong.FontFamily = font;

            TileNotes.Visibility = Vis(S.ShowNotes);
            TileBattery.Visibility = Vis(S.ShowBattery);
            TileImage.Visibility = Vis(S.ShowImage);
            TileWeather.Visibility = Vis(S.ShowWeather);
            TileClock.Visibility = Vis(S.ShowClock);
            TileCalendar.Visibility = Vis(S.ShowCalendar);

            ApplyZOrder();
            BuildCalendarHeader();
            BuildCalendar();
            UpdateClock();
            ScheduleNextTick();
            if (_lastWeather != null) ShowWeather(_lastWeather);
            RenderBatteries();
        }

        private static Visibility Vis(bool v) => v ? Visibility.Visible : Visibility.Collapsed;

        private static CultureInfo SafeCulture(string name)
        {
            try { return CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(name) ? "pt-BR" : name); }
            catch { return CultureInfo.CurrentCulture; }
        }

        private string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], _culture) + s.Substring(1);

        // ================== Relógio ==================

        private void ClockTick(object sender, EventArgs e)
        {
            _clockTimer.Stop();
            UpdateClock();
            ScheduleNextTick();
        }

        /// <summary>Agenda o próximo tique exatamente na virada do segundo/minuto (nada de polling).</summary>
        private void ScheduleNextTick()
        {
            var now = DateTime.Now;
            TimeSpan wait = S.ShowSeconds
                ? TimeSpan.FromMilliseconds(1000 - now.Millisecond + 5)
                : TimeSpan.FromMilliseconds((60 - now.Second) * 1000 - now.Millisecond + 20);
            _clockTimer.Stop();
            _clockTimer.Interval = wait;
            _clockTimer.Start();
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
                    ClockText.Inlines.Add(new Run(" " + ampm) { FontSize = Math.Max(10, ClockText.FontSize * 0.28) });
            }

            var dtf = _culture.DateTimeFormat;
            string md = dtf.ShortDatePattern.TrimStart().StartsWith("M") ? "MM/dd" : "dd/MM";
            DateShort.Text = now.ToString(md, _culture);
            DateLong.Text = Cap(dtf.GetDayName(now.DayOfWeek)) + ", " + dtf.GetMonthName(now.Month);

            if (now.Date != _lastDay)
            {
                _lastDay = now.Date;
                _calMonth = new DateTime(now.Year, now.Month, 1);
                BuildCalendar();
            }
        }

        // ================== Calendário ==================

        private void BuildCalendarHeader()
        {
            CalHeader.Children.Clear();
            var dtf = _culture.DateTimeFormat;
            int first = (int)dtf.FirstDayOfWeek;
            for (int i = 0; i < 7; i++)
            {
                var dow = (DayOfWeek)((first + i) % 7);
                string name = dtf.AbbreviatedDayNames[(int)dow].TrimEnd('.');
                if (name.Length > 3) name = name.Substring(0, 3);
                CalHeader.Children.Add(new TextBlock
                {
                    Text = Cap(name),
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Opacity = 0.9
                });
            }
        }

        private void BuildCalendar()
        {
            CalDays.Children.Clear();
            var today = DateTime.Today;
            var m = _calMonth;
            CalTitle.Text = Cap(m.ToString("MMMM yyyy", _culture));

            int first = (int)_culture.DateTimeFormat.FirstDayOfWeek;
            int offset = ((int)m.DayOfWeek - first + 7) % 7;
            int days = DateTime.DaysInMonth(m.Year, m.Month);
            int rows = (int)Math.Ceiling((offset + days) / 7.0);
            CalDays.Rows = rows;

            var accent = (Brush)Resources["AccentBrush"];
            var accentText = (Brush)Resources["AccentTextBrush"];

            for (int i = 0; i < rows * 7; i++)
            {
                int d = i - offset + 1;
                var cell = new Grid { Height = 31 };
                if (d >= 1 && d <= days)
                {
                    bool isToday = m.Year == today.Year && m.Month == today.Month && d == today.Day;
                    var tb = new TextBlock
                    {
                        Text = d.ToString(CultureInfo.InvariantCulture),
                        FontSize = 16,
                        FontWeight = FontWeights.Light,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    if (isToday)
                    {
                        cell.Children.Add(new Ellipse { Width = 29, Height = 29, Fill = accent });
                        tb.Foreground = accentText;
                        tb.FontWeight = FontWeights.SemiBold;
                    }
                    cell.Children.Add(tb);
                }
                CalDays.Children.Add(cell);
            }
        }

        private void CalPrev_Click(object sender, RoutedEventArgs e) { _calMonth = _calMonth.AddMonths(-1); BuildCalendar(); }
        private void CalNext_Click(object sender, RoutedEventArgs e) { _calMonth = _calMonth.AddMonths(1); BuildCalendar(); }

        private void CalTitle_Click(object sender, MouseButtonEventArgs e)
        {
            if (_dragged) return;
            _calMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            BuildCalendar();
        }

        // ================== Clima ==================

        public async void RefreshWeather()
        {
            if (_weatherBusy || !S.ShowWeather) return;
            _weatherBusy = true;
            try
            {
                var w = await WeatherService.GetCurrentAsync(S.Latitude, S.Longitude, S.TemperatureUnit);
                _lastWeather = w;
                ShowWeather(w);
                _weatherTimer.Interval = TimeSpan.FromMinutes(30);
            }
            catch (Exception ex)
            {
                SettingsService.Log(ex);
                WeatherCity.Text = S.City + " (sem conexão)";
                TileWeather.ToolTip = "Não foi possível atualizar o clima. Nova tentativa em 5 minutos.";
                _weatherTimer.Interval = TimeSpan.FromMinutes(5);
            }
            finally
            {
                _weatherBusy = false;
            }
        }

        private void ShowWeather(WeatherNow w)
        {
            WeatherIcon.Source = WeatherIcons.Get(WeatherIcons.KindFromCode(w.Code), w.IsDay);
            WeatherTemp.Inlines.Clear();
            WeatherTemp.Inlines.Add(new Run(((int)Math.Round(w.Temperature)).ToString(_culture)));
            WeatherTemp.Inlines.Add(new Run(S.TemperatureUnit == "fahrenheit" ? "°F" : "°C") { FontSize = 18 });
            WeatherCity.Text = S.City;
            TileWeather.ToolTip = WeatherService.Describe(w.Code) + " em " + S.City + " (clique para atualizar)";
        }

        private void Weather_Click(object sender, MouseButtonEventArgs e)
        {
            if (_dragged) return;
            RefreshWeather();
        }

        // ================== Bateria ==================

        public async void RefreshBattery()
        {
            if (_batteryBusy || !S.ShowBattery) return;
            _batteryBusy = true;
            try
            {
                var list = await Task.Run(() => BatteryService.GetAllAsync());
                _devices = list;
                RenderBatteries();
                DevicesUpdated?.Invoke();
            }
            catch (Exception ex) { SettingsService.Log(ex); }
            finally { _batteryBusy = false; }
        }

        public void RefreshAll()
        {
            RefreshWeather();
            RefreshBattery();
        }

        public void RenderBatteries()
        {
            BatteryGrid.Children.Clear();
            var visible = _devices.Where(d => !S.HiddenDevices.Contains(d.Key)).ToList();
            BatteryEmpty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            BatteryGrid.Columns = visible.Count <= 1 ? 1 : visible.Count <= 4 ? 2 : 3;
            foreach (var d in visible) BatteryGrid.Children.Add(BuildBatteryItem(d));
        }

        private UIElement BuildBatteryItem(DeviceBattery d)
        {
            const double size = 36, stroke = 3.5;
            double r = (size - stroke) / 2;
            var center = new Point(size / 2, size / 2);
            var textColor = ColorUtil.Parse(S.TextColor, Colors.White);

            var ring = new Grid { Width = size, Height = size };
            ring.Children.Add(new Ellipse { Stroke = ColorUtil.Solid(textColor, 0.22), StrokeThickness = stroke });
            ring.Children.Add(new Path
            {
                Data = ArcGeometry(center, r, d.Level),
                Stroke = RingBrush(d.Level),
                StrokeThickness = stroke,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
            ring.Children.Add(new TextBlock
            {
                Text = d.Glyph,
                FontFamily = (FontFamily)Resources["IconFont"],
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            var stack = new StackPanel { Margin = new Thickness(5, 3, 5, 3), ToolTip = d.Name + ": " + d.Level + "%" };
            stack.Children.Add(ring);
            stack.Children.Add(new TextBlock
            {
                Text = d.Level + "%",
                FontSize = 11,
                Opacity = 0.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });
            return stack;
        }

        private static Brush RingBrush(int level)
        {
            Color c = level > 50 ? Color.FromRgb(0x5B, 0xD1, 0x6A)
                    : level > 20 ? Color.FromRgb(0xFF, 0xC9, 0x3C)
                    : Color.FromRgb(0xFF, 0x5A, 0x5F);
            return ColorUtil.Solid(c);
        }

        private static Geometry ArcGeometry(Point c, double r, int level)
        {
            level = Math.Clamp(level, 0, 100);
            if (level >= 100) return new EllipseGeometry(c, r, r);
            if (level <= 0) return Geometry.Empty;

            double a = level / 100.0 * 2 * Math.PI;
            var start = new Point(c.X, c.Y - r);
            var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
            var fig = new PathFigure { StartPoint = start, IsClosed = false };
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            g.Freeze();
            return g;
        }

        // ================== Imagem ==================

        private void ApplyImage()
        {
            bool has = false;
            if (!string.IsNullOrWhiteSpace(S.ImagePath) && System.IO.File.Exists(S.ImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad; // não trava o arquivo
                    bmp.UriSource = new Uri(S.ImagePath);
                    // Decodifica pequeno: uma foto de 4000px não ocupa 60 MB de RAM à toa
                    if (!S.ImagePixelArt) bmp.DecodePixelWidth = 260;
                    bmp.EndInit();
                    bmp.Freeze();

                    var mode = S.ImagePixelArt ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;
                    var brush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                    RenderOptions.SetBitmapScalingMode(brush, mode);
                    brush.Freeze();
                    RenderOptions.SetBitmapScalingMode(TileImage, mode);

                    TileImage.Background = brush;
                    has = true;
                }
                catch (Exception ex) { SettingsService.Log(ex); }
            }

            if (!has) TileImage.Background = _tileBrush;
            ImagePlaceholder.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            TileImage.Cursor = has ? null : Cursors.Hand;
        }

        public void PickImage()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Escolher imagem do widget",
                Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|Todos os arquivos|*.*"
            };
            _allowFront = true;
            try
            {
                if (dlg.ShowDialog(this) == true)
                {
                    S.ImagePath = dlg.FileName;
                    SettingsService.SaveSoon();
                    ApplyImage();
                    ResourceSaver.Trim();
                }
            }
            finally
            {
                _allowFront = NotesOverlay.Visibility == Visibility.Visible;
                ApplyZOrder();
            }
        }

        public void ClearImage()
        {
            S.ImagePath = "";
            SettingsService.SaveSoon();
            ApplyImage();
        }

        private void Image_Click(object sender, MouseButtonEventArgs e)
        {
            if (_dragged) return;
            if (string.IsNullOrWhiteSpace(S.ImagePath)) PickImage();
        }

        // ================== Notas ==================

        private void UpdateNotesSummary()
        {
            int total = SettingsService.Notes.Count;
            int done = SettingsService.Notes.Count(n => n.Done);
            NotesCount.Text = total == 0 ? "" : done + "/" + total;
            NotesEmpty.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void OpenNotes()
        {
            NotesOverlay.Visibility = Visibility.Visible;
            _allowFront = true;
            Activate();
            NewNoteBox.Focus();
            Keyboard.Focus(NewNoteBox);
        }

        private void CloseNotes()
        {
            NotesOverlay.Visibility = Visibility.Collapsed;
            _allowFront = false;
            // Remove itens vazios que sobraram da edição
            foreach (var n in SettingsService.Notes.Where(n => string.IsNullOrWhiteSpace(n.Text)).ToList())
                SettingsService.Notes.Remove(n);
            ApplyZOrder();
            SettingsService.SaveSoon();
        }

        private void Notes_Click(object sender, MouseButtonEventArgs e)
        {
            if (_dragged) return;
            OpenNotes();
        }

        private void NotesClose_Click(object sender, RoutedEventArgs e) => CloseNotes();

        private void ClearDone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var n in SettingsService.Notes.Where(n => n.Done).ToList())
                SettingsService.Notes.Remove(n);
        }

        private void DeleteNote_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is NoteItem item)
                SettingsService.Notes.Remove(item);
        }

        private void NewNoteBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string t = NewNoteBox.Text.Trim();
                if (t.Length > 0) SettingsService.Notes.Add(new NoteItem { Text = t });
                NewNoteBox.Clear();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseNotes();
                e.Handled = true;
            }
        }

        private void NewNoteBox_TextChanged(object sender, TextChangedEventArgs e)
            => NewNotePlaceholder.Visibility = NewNoteBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && NotesOverlay.Visibility == Visibility.Visible) CloseNotes();
        }

        // ================== Arrastar / redimensionar ==================

        private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragged = false;
            _downPos = e.GetPosition(this);
            _maybeDrag = !S.Locked && !IsInteractive(e.OriginalSource as DependencyObject);
        }

        private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_maybeDrag || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _downPos.X) < 4 && Math.Abs(p.Y - _downPos.Y) < 4) return;

            _maybeDrag = false;
            _dragged = true;
            try { DragMove(); } catch (InvalidOperationException) { }
        }

        private static bool IsInteractive(DependencyObject d)
        {
            while (d != null)
            {
                if (d is TextBoxBase || d is ButtonBase || d is ScrollBar || d is Thumb) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        /// <summary>Ctrl + roda do mouse muda o tamanho do widget.</summary>
        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && !S.Locked)
            {
                S.Scale = Math.Round(Math.Clamp(S.Scale + (e.Delta > 0 ? 0.05 : -0.05), 0.5, 2.5), 2);
                RootScale.ScaleX = S.Scale;
                RootScale.ScaleY = S.Scale;
                SettingsService.SaveSoon();
                e.Handled = true;
                return;
            }
            base.OnPreviewMouseWheel(e);
        }

        // ================== Menu do botão direito ==================

        private void WidgetMenu_Opened(object sender, RoutedEventArgs e)
        {
            MenuLock.IsChecked = S.Locked;
            MenuRemoveImage.IsEnabled = !string.IsNullOrWhiteSpace(S.ImagePath);
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e) => App.Instance.OpenSettings();
        private void MenuNotes_Click(object sender, RoutedEventArgs e) => OpenNotes();
        private void MenuPickImage_Click(object sender, RoutedEventArgs e) => PickImage();
        private void MenuRemoveImage_Click(object sender, RoutedEventArgs e) => ClearImage();
        private void MenuRefresh_Click(object sender, RoutedEventArgs e) => RefreshAll();
        private void MenuExit_Click(object sender, RoutedEventArgs e) => App.Instance.ExitApp();

        private void MenuLock_Click(object sender, RoutedEventArgs e)
        {
            S.Locked = MenuLock.IsChecked;
            SettingsService.SaveSoon();
        }
    }
}
