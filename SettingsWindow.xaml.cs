using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;
using DeskWidgets.Controls;
using DeskWidgets.Helpers;
using DeskWidgets.Models;
using DeskWidgets.Services;
using DeskWidgets.Widgets;

namespace DeskWidgets
{
    public partial class SettingsWindow : Window
    {
        private bool _loading = true; // evita aplicar mudanças enquanto os controles são preenchidos
        private static AppSettings S => SettingsService.Current;

        private BatteryWidget Battery => WidgetManager.Get<BatteryWidget>();
        private HardwareWidget Hardware => WidgetManager.Get<HardwareWidget>();
        private WeatherWidget Weather => WidgetManager.Get<WeatherWidget>();
        private ImageWidget ImageW => WidgetManager.Get<ImageWidget>();

        private StackPanel[] Pages => new[] { PageThemes, PageWidgets, PageClock, PageWeather, PageMusic, PagePomodoro, PageHardware, PageNotes, PageImage, PageDevices, PageGeneral };

        public SettingsWindow()
        {
            InitializeComponent();

            var fonts = Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            FontBox.ItemsSource = fonts;
            UiFontBox.ItemsSource = fonts;

            var v = typeof(App).Assembly.GetName().Version;
            if (v != null) VersionText.Text = $"versão {v.Major}.{v.Minor}";

            LoadFromSettings();
            if (!string.IsNullOrEmpty(TodoistSync.Token)) _ = LoadTodoistProjectsAsync(TodoistSync.Token, silent: true);

            if (Battery != null) Battery.DevicesUpdated += BuildDevices;
            if (Hardware != null) Hardware.Updated += UpdateHardwareStatus;
            TodoistSync.StatusChanged += OnTodoistStatus;
            WidgetManager.LayoutChanged += OnLayoutChanged;

            SourceInitialized += (s, e) => DarkTitleBar();
            Closed += (s, e) =>
            {
                if (Battery != null) Battery.DevicesUpdated -= BuildDevices;
                if (Hardware != null) Hardware.Updated -= UpdateHardwareStatus;
                TodoistSync.StatusChanged -= OnTodoistStatus;
                WidgetManager.LayoutChanged -= OnLayoutChanged;
                SettingsService.SaveNow();
                ResourceSaver.Trim(); // libera a lista de fontes etc.
            };
        }

        /// <summary>Barra de título escura (Windows 10 20H1+ / 11).</summary>
        private void DarkTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int on = 1;
                if (NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                    NativeMethods.DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
                int caption = 0x00120D0D; // COLORREF (BGR) = #0D0D12
                NativeMethods.DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
            }
            catch { }
        }

        private void LoadFromSettings()
        {
            _loading = true;

            // Temas
            BuildPalettes();
            TopHex.Text = S.GradientTop;
            BottomHex.Text = S.GradientBottom;
            AccentHex.Text = S.Accent;
            TextHex.Text = S.TextColor;
            UpdateSwatches();
            DirVertical.IsChecked = S.GradientDirection == "Vertical" || string.IsNullOrEmpty(S.GradientDirection);
            DirHorizontal.IsChecked = S.GradientDirection == "Horizontal";
            DirDiagonal.IsChecked = S.GradientDirection == "Diagonal";
            OpacitySlider.Value = Math.Round(S.TileOpacity * 100);
            RadiusSlider.Value = S.CornerRadius;
            BorderSlider.Value = Math.Round(S.BorderOpacity * 100);
            UiFontBox.SelectedItem = (UiFontBox.ItemsSource as List<string>)?
                .FirstOrDefault(f => string.Equals(f, S.UiFont, StringComparison.OrdinalIgnoreCase));
            AnimatedCheck.IsChecked = S.AnimatedIcons;
            UpdateDemoIcons();

            // Widgets
            BuildWidgetList();
            EditModeCheck.IsChecked = WidgetManager.EditMode;
            EditModeQuick.IsChecked = WidgetManager.EditMode;
            SnapWidgetsCheck.IsChecked = S.SnapToWidgets;
            SnapGridCheck.IsChecked = S.SnapToGrid;
            GridSlider.Value = S.GridSize;
            GapSlider.Value = S.WidgetGap;
            SelectByTag(ZModeBox, S.ZMode);
            LockCheck.IsChecked = S.Locked;

            // Relógio
            FontBox.SelectedItem = (FontBox.ItemsSource as List<string>)?
                .FirstOrDefault(f => string.Equals(f, S.ClockFont, StringComparison.OrdinalIgnoreCase));
            SetPreviewFont();
            BoldCheck.IsChecked = S.ClockBold;
            H24Check.IsChecked = S.Clock24h;
            SecondsCheck.IsChecked = S.ShowSeconds;
            ShowDateCheck.IsChecked = S.ClockShowDate;
            SelectByTag(CultureBox, S.DateCulture);

            // Clima
            CurrentCity.Text = "Cidade atual: " + S.City;
            SelectByTag(UnitBox, S.TemperatureUnit);
            WeatherBgCheck.IsChecked = S.WeatherTransparentTile;

            // Música
            MusicArtCheck.IsChecked = S.MusicShowArt;
            MusicBackdropCheck.IsChecked = S.MusicArtBackground;
            MusicProgressCheck.IsChecked = S.MusicShowProgress;

            // Pomodoro
            FocusSlider.Value = S.PomodoroFocus;
            ShortSlider.Value = S.PomodoroShort;
            LongSlider.Value = S.PomodoroLong;
            CyclesSlider.Value = S.PomodoroCycles;
            PomoAutoCheck.IsChecked = S.PomodoroAutoStart;
            PomoSoundCheck.IsChecked = S.PomodoroSound;
            PomoNotifyCheck.IsChecked = S.PomodoroNotify;

            // Componentes
            HwCpuTempCheck.IsChecked = S.HwShowCpuTemp;
            HwGpuTempCheck.IsChecked = S.HwShowGpuTemp;
            HwDiskCheck.IsChecked = S.HwShowDiskTemp;
            HwLoadCheck.IsChecked = S.HwShowCpuLoad;
            HwRamCheck.IsChecked = S.HwShowRam;
            HwRefreshSlider.Value = S.HardwareRefreshSeconds;
            HwWarnSlider.Value = S.HardwareWarnTemp;
            UpdateHardwareStatus();

            // Notas, imagem, dispositivos, geral
            UpdateTodoistUi();
            PixelArtCheck.IsChecked = S.ImagePixelArt;
            BuildDevices();
            StartupCheck.IsChecked = StartupService.IsEnabled();
            StartMenuCheck.IsChecked = S.StartMenuShortcut && ShortcutService.Exists();

            UpdateValueLabels();
            UpdatePreview();
            _loading = false;
        }

        private void ApplyAll()
        {
            if (_loading) return;
            WidgetManager.ApplyAll();
            UpdatePreview();
            SettingsService.SaveSoon();
        }

        private void Apply(string widgetId)
        {
            if (_loading) return;
            WidgetManager.Apply(widgetId);
            SettingsService.SaveSoon();
        }

        // ================== Navegação ==================

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (PageThemes == null) return; // ainda carregando o XAML
            var map = new Dictionary<object, StackPanel>
            {
                [NavThemes] = PageThemes, [NavWidgets] = PageWidgets, [NavClock] = PageClock, [NavWeather] = PageWeather,
                [NavMusic] = PageMusic, [NavPomodoro] = PagePomodoro, [NavHardware] = PageHardware, [NavNotes] = PageNotes,
                [NavImage] = PageImage, [NavDevices] = PageDevices, [NavGeneral] = PageGeneral
            };
            if (!map.TryGetValue(sender, out var page)) return;
            foreach (var p in Pages) p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
            PageScroll.ScrollToTop();
            if (page == PageHardware) Hardware?.Refresh();
        }

        // ================== Utilitários ==================

        private static void SelectByTag(ComboBox box, string tag)
        {
            foreach (var item in box.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedItem = item;
                    return;
                }
            }
            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        private static string TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

        private void UpdateValueLabels()
        {
            if (OpacityValue == null) return;
            OpacityValue.Text = (int)OpacitySlider.Value + "%";
            RadiusValue.Text = (int)RadiusSlider.Value + " px";
            BorderValue.Text = (int)BorderSlider.Value == 0 ? "sem" : (int)BorderSlider.Value + "%";
            GridValue.Text = (int)GridSlider.Value + " px";
            GapValue.Text = (int)GapSlider.Value + " px";
            FocusValue.Text = (int)FocusSlider.Value + " min";
            ShortValue.Text = (int)ShortSlider.Value + " min";
            LongValue.Text = (int)LongSlider.Value + " min";
            CyclesValue.Text = ((int)CyclesSlider.Value).ToString();
            HwRefreshValue.Text = (int)HwRefreshSlider.Value + " s";
            HwWarnValue.Text = (int)HwWarnSlider.Value + " °C";
        }

        private void UpdateSwatches()
        {
            TopSwatch.Background = ColorUtil.Solid(ColorUtil.Parse(S.GradientTop, Colors.Black));
            BottomSwatch.Background = ColorUtil.Solid(ColorUtil.Parse(S.GradientBottom, Colors.Black));
            AccentSwatch.Background = ColorUtil.Solid(ColorUtil.Parse(S.Accent, Colors.White));
            TextSwatch.Background = ColorUtil.Solid(ColorUtil.Parse(S.TextColor, Colors.White));
        }

        private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
            e.Handled = true;
        }

        // ================== Prévia do tema ==================

        private void UpdatePreview()
        {
            if (PreviewClock == null) return;
            var t = S.GlobalTheme();
            var bg = ColorUtil.Gradient(t, S.TileOpacity, S.GradientDirection);
            var text = ColorUtil.Parse(t.Text, Colors.White);
            var accent = ColorUtil.Parse(t.Accent, Colors.White);
            var textBrush = ColorUtil.Solid(text);
            var border = S.BorderOpacity > 0.001 ? ColorUtil.Solid(text, S.BorderOpacity * Math.Max(0.35, S.TileOpacity)) : Brushes.Transparent;
            double r = Math.Clamp(S.CornerRadius, 0, 40);

            foreach (var b in new[] { PreviewClock, PreviewMusic })
            {
                b.Background = bg;
                b.CornerRadius = new CornerRadius(r);
                b.BorderBrush = border;
                b.BorderThickness = new Thickness(1);
                TextElement.SetForeground(b, textBrush);
            }
            try { PreviewTime.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(S.ClockFont) ? "Times New Roman" : S.ClockFont); } catch { }
            PreviewTime.FontWeight = S.ClockBold ? FontWeights.Bold : FontWeights.Normal;
            PreviewDate.FontFamily = PreviewTime.FontFamily;
            try
            {
                var ui = new FontFamily((string.IsNullOrWhiteSpace(S.UiFont) ? "Segoe UI Variable Text" : S.UiFont) + ", Segoe UI");
                PreviewTitle.FontFamily = ui;
                PreviewArtist.FontFamily = ui;
            }
            catch { }
            PreviewPlay.Background = ColorUtil.Solid(accent);
            PreviewPlayGlyph.Foreground = ColorUtil.ContrastFor(accent);
            PreviewArt.Background = new LinearGradientBrush(ColorUtil.Mix(accent, Colors.Black, 0.25), ColorUtil.Mix(accent, Colors.White, 0.1), 45);
        }

        // ================== Galeria de temas ==================

        private void BuildPalettes()
        {
            PalettePanel.Children.Clear();
            foreach (var p in ThemeService.AllThemes)
            {
                bool selected = string.Equals(S.PaletteName, p.Name, StringComparison.Ordinal);
                bool custom = ThemeService.IsCustom(p.Name);
                var accent = ColorUtil.Parse(p.Accent, Colors.White);
                var text = ColorUtil.Parse(p.Text, Colors.White);

                var inner = new Grid();
                inner.Children.Add(new TextBlock
                {
                    Text = "12:30",
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = ColorUtil.Solid(text),
                    Margin = new Thickness(10, 8, 0, 0)
                });
                inner.Children.Add(new Border
                {
                    Width = 22, Height = 6, CornerRadius = new CornerRadius(3),
                    Background = ColorUtil.Solid(accent),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(10, 0, 0, 10)
                });

                var swatch = new Border
                {
                    Width = 104,
                    Height = 66,
                    CornerRadius = new CornerRadius(11),
                    Background = new LinearGradientBrush(new GradientStopCollection
                    {
                        new GradientStop(ColorUtil.Parse(p.Top, Colors.Black), 0),
                        new GradientStop(ColorUtil.Parse(p.Bottom, Colors.Black), 1)
                    }, new Point(0, 0), new Point(1, 1)),
                    BorderBrush = selected ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(selected ? 2.5 : 1),
                    Child = inner
                };

                var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 14) };
                var swatchHost = new Grid();
                swatchHost.Children.Add(swatch);
                if (custom)
                {
                    var del = new Button
                    {
                        Content = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 9,
                        Padding = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 4, 4, 0), Tag = p, ToolTip = "Excluir tema"
                    };
                    del.Click += DeleteTheme_Click;
                    swatchHost.Children.Add(del);
                }
                stack.Children.Add(swatchHost);
                stack.Children.Add(new TextBlock
                {
                    Text = p.Name + (custom ? " ★" : ""),
                    FontSize = 12,
                    Foreground = selected ? (Brush)FindResource("Fg") : (Brush)FindResource("Muted"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                });

                var btn = new Button { Style = (Style)FindResource("PlainButton"), Content = stack, Tag = p, ToolTip = "Usar o tema " + p.Name };
                btn.Click += Palette_Click;
                PalettePanel.Children.Add(btn);
            }
        }

        private void Palette_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is ThemeDef p)) return;
            S.PaletteName = p.Name;
            S.GradientTop = p.Top;
            S.GradientBottom = p.Bottom;
            S.Accent = p.Accent;
            S.TextColor = p.Text;

            _loading = true;
            TopHex.Text = p.Top;
            BottomHex.Text = p.Bottom;
            AccentHex.Text = p.Accent;
            TextHex.Text = p.Text;
            BuildPalettes();
            UpdateSwatches();
            _loading = false;
            ApplyAll();
        }

        private void SaveTheme_Click(object sender, RoutedEventArgs e)
        {
            string name = ThemeNameBox.Text.Trim();
            if (name.Length == 0)
            {
                ThemeSaveStatus.Text = "Escreva um nome para o tema.";
                return;
            }
            if (Palettes.All.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                ThemeSaveStatus.Text = "Já existe um tema pronto com esse nome. Escolha outro.";
                return;
            }

            S.CustomThemes ??= new List<ThemeDef>();
            var existing = S.CustomThemes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            var def = new ThemeDef(name, S.GradientTop, S.GradientBottom, S.Accent, S.TextColor);
            if (existing != null) S.CustomThemes[S.CustomThemes.IndexOf(existing)] = def;
            else S.CustomThemes.Add(def);
            S.PaletteName = name;

            ThemeNameBox.Clear();
            ThemeSaveStatus.Text = existing != null ? $"Tema “{name}” atualizado." : $"Tema “{name}” salvo na galeria.";
            BuildPalettes();
            WidgetManager.ApplyAll(); // widgets que usam esse tema por nome
            SettingsService.SaveSoon();
        }

        private void DeleteTheme_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!((sender as Button)?.Tag is ThemeDef t)) return;
            var r = MessageBox.Show(this, $"Excluir o tema “{t.Name}”? Widgets que usam esse tema voltam ao tema global.",
                "Excluir tema", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            S.CustomThemes.RemoveAll(x => string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var l in S.Widgets.Values.Where(l => string.Equals(l.Theme, t.Name, StringComparison.OrdinalIgnoreCase)))
                l.Theme = "";
            BuildPalettes();
            BuildWidgetList();
            WidgetManager.ApplyAll();
            SettingsService.SaveSoon();
        }

        // ================== Cores personalizadas ==================

        private TextBox HexBoxFor(string tag) => tag switch
        {
            "Top" => TopHex,
            "Bottom" => BottomHex,
            "Accent" => AccentHex,
            _ => TextHex
        };

        private void Hex_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var tb = (TextBox)sender;
            if (!ColorUtil.TryParse(tb.Text, out var c)) return;

            string hex = ColorUtil.ToHex(c);
            switch (tb.Tag as string)
            {
                case "Top": S.GradientTop = hex; break;
                case "Bottom": S.GradientBottom = hex; break;
                case "Accent": S.Accent = hex; break;
                default: S.TextColor = hex; break;
            }

            if (S.PaletteName != "Personalizada")
            {
                S.PaletteName = "Personalizada";
                _loading = true;
                BuildPalettes();
                _loading = false;
            }
            UpdateSwatches();
            ApplyAll();
        }

        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            string tag = (sender as Button)?.Tag as string ?? "Top";
            var box = HexBoxFor(tag);
            var current = ColorUtil.Parse(box.Text, Colors.White);

            using var dlg = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // mantém a transparência (temas tipo "Vidro")
                string alpha = current.A == 255 ? "" : $"{current.A:X2}";
                box.Text = $"#{alpha}{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}"; // dispara Hex_TextChanged
            }
        }

        // ================== Estilo ==================

        private void Direction_Click(object sender, RoutedEventArgs e)
        {
            S.GradientDirection = (sender as FrameworkElement)?.Tag as string ?? "Vertical";
            ApplyAll();
        }

        private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            S.TileOpacity = OpacitySlider.Value / 100.0;
            UpdateValueLabels();
            ApplyAll();
        }

        private void Radius_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            S.CornerRadius = RadiusSlider.Value;
            UpdateValueLabels();
            ApplyAll();
        }

        private void Border_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            S.BorderOpacity = BorderSlider.Value / 100.0;
            UpdateValueLabels();
            ApplyAll();
        }

        private void UiFont_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || !(UiFontBox.SelectedItem is string name)) return;
            S.UiFont = name;
            ApplyAll();
        }

        private void Animated_Click(object sender, RoutedEventArgs e)
        {
            S.AnimatedIcons = AnimatedCheck.IsChecked == true;
            AnimatedIcon.Enabled = S.AnimatedIcons;
            UpdateDemoIcons();
            SettingsService.SaveSoon();
        }

        private void UpdateDemoIcons()
        {
            foreach (var i in new[] { Demo1, Demo2, Demo3, Demo4, Demo5, Demo6, Demo7, Demo8 })
                i.IsActive = S.AnimatedIcons;
        }

        // ================== Widgets e layout ==================

        private void OnLayoutChanged()
        {
            _loading = true;
            EditModeCheck.IsChecked = WidgetManager.EditMode;
            EditModeQuick.IsChecked = WidgetManager.EditMode;
            LockCheck.IsChecked = S.Locked;
            BuildWidgetList();
            _loading = false;
        }

        private static readonly Dictionary<string, string> WidgetDescriptions = new Dictionary<string, string>
        {
            ["clock"] = "Hora e data com a fonte que você quiser",
            ["calendar"] = "Mês inteiro com o dia de hoje destacado",
            ["weather"] = "Temperatura da sua cidade",
            ["notes"] = "Checklist editável, com Todoist opcional",
            ["battery"] = "Fones, mouses, teclados e controles",
            ["image"] = "Uma foto ou arte sua",
            ["music"] = "Tocando agora, com play/pause, próxima e voltar",
            ["pomodoro"] = "Timer de foco e pausas",
            ["hardware"] = "Temperatura da CPU/GPU, uso e memória"
        };

        private void BuildWidgetList()
        {
            WidgetList.Children.Clear();
            var themeNames = new List<string> { "Tema global" };
            themeNames.AddRange(ThemeService.AllThemes.Select(t => t.Name));

            bool first = true;
            foreach (var w in WidgetManager.All)
            {
                if (!first) WidgetList.Children.Add(new Border { Style = (Style)FindResource("Divider") });
                first = false;

                var layout = S.Layout(w.Id);
                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };

                var toggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = layout.Visible, Tag = w.Id, ToolTip = "Mostrar / ocultar" };
                toggle.Click += WidgetToggle_Click;
                DockPanel.SetDock(toggle, Dock.Right);
                row.Children.Add(toggle);

                var theme = new ComboBox { Width = 160, Margin = new Thickness(0, 0, 16, 0), Tag = w.Id, ToolTip = "Tema só deste widget", VerticalAlignment = VerticalAlignment.Center };
                foreach (var n in themeNames) theme.Items.Add(n);
                theme.SelectedItem = string.IsNullOrEmpty(layout.Theme) || ThemeService.Find(layout.Theme) == null ? "Tema global" : ThemeService.Find(layout.Theme).Name;
                theme.SelectionChanged += WidgetTheme_Changed;
                DockPanel.SetDock(theme, Dock.Right);
                row.Children.Add(theme);

                var locate = new Button
                {
                    Content = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 12, Padding = new Thickness(9, 7, 9, 7),
                    Margin = new Thickness(0, 0, 8, 0), Tag = w.Id, ToolTip = "Trazer para frente", VerticalAlignment = VerticalAlignment.Center
                };
                locate.Click += WidgetLocate_Click;
                DockPanel.SetDock(locate, Dock.Right);
                row.Children.Add(locate);

                var icon = new Border
                {
                    Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 14, 0),
                    Background = (Brush)FindResource("AccentSoft"),
                    Child = new TextBlock
                    {
                        Text = w.Glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 16,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xB0, 0xFF))
                    }
                };
                DockPanel.SetDock(icon, Dock.Left);
                row.Children.Add(icon);

                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = w.DisplayName, Style = (Style)FindResource("RowTitle") });
                text.Children.Add(new TextBlock
                {
                    Text = WidgetDescriptions.TryGetValue(w.Id, out var d) ? d : "",
                    Style = (Style)FindResource("RowDesc")
                });
                row.Children.Add(text);

                WidgetList.Children.Add(row);
            }
        }

        private void WidgetToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox cb) || !(cb.Tag is string id)) return;
            WidgetManager.SetVisible(id, cb.IsChecked == true);
        }

        private void WidgetTheme_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || !(sender is ComboBox box) || !(box.Tag is string id)) return;
            string name = box.SelectedItem as string;
            WidgetManager.SetWidgetTheme(id, name == "Tema global" ? "" : name);
        }

        private void WidgetLocate_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is string id)) return;
            if (!S.Layout(id).Visible) WidgetManager.SetVisible(id, true);
            WidgetManager.HostOf(id)?.BringToFront(activate: false);
        }

        private void EditMode_Click(object sender, RoutedEventArgs e) => WidgetManager.EditMode = EditModeCheck.IsChecked == true;
        private void EditModeQuick_Click(object sender, RoutedEventArgs e) => WidgetManager.EditMode = EditModeQuick.IsChecked == true;

        private void Snap_Click(object sender, RoutedEventArgs e)
        {
            S.SnapToWidgets = SnapWidgetsCheck.IsChecked == true;
            S.SnapToGrid = SnapGridCheck.IsChecked == true;
            SettingsService.SaveSoon();
        }

        private void Grid_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            S.GridSize = (int)GridSlider.Value;
            UpdateValueLabels();
            SettingsService.SaveSoon();
        }

        private void Gap_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            S.WidgetGap = GapSlider.Value;
            UpdateValueLabels();
            SettingsService.SaveSoon();
        }

        private void AutoArrange_Click(object sender, RoutedEventArgs e) => WidgetManager.AutoArrange();

        private void ShowAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var w in WidgetManager.All.ToList()) WidgetManager.SetVisible(w.Id, true);
        }

        private void ResetLayout_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(this, "Voltar todos os widgets para a posição e o tamanho padrão?",
                "Restaurar layout", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes) WidgetManager.ResetLayout();
        }

        private void ZMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            S.ZMode = TagOf(ZModeBox) ?? "Desktop";
            ApplyAll();
        }

        private void Lock_Click(object sender, RoutedEventArgs e) => WidgetManager.SetLocked(LockCheck.IsChecked == true);

        // ================== Relógio ==================

        private void SetPreviewFont()
        {
            string name = FontBox.SelectedItem as string ?? S.ClockFont;
            try { FontPreview.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(name) ? "Times New Roman" : name); } catch { }
            FontPreview.FontWeight = S.ClockBold ? FontWeights.Bold : FontWeights.Normal;
        }

        private void Font_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (FontBox.SelectedItem is string name)
            {
                S.ClockFont = name;
                SetPreviewFont();
                Apply("clock");
                UpdatePreview();
            }
        }

        private void ClockOptions_Click(object sender, RoutedEventArgs e)
        {
            S.ClockBold = BoldCheck.IsChecked == true;
            S.Clock24h = H24Check.IsChecked == true;
            S.ShowSeconds = SecondsCheck.IsChecked == true;
            S.ClockShowDate = ShowDateCheck.IsChecked == true;
            SetPreviewFont();
            Apply("clock");
            UpdatePreview();
        }

        private void Culture_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            S.DateCulture = TagOf(CultureBox) ?? "pt-BR";
            Apply("clock");
            Apply("calendar");
        }

        // ================== Clima ==================

        private void CityBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Search_Click(sender, e); e.Handled = true; }
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            string q = CityBox.Text.Trim();
            if (q.Length < 2)
            {
                SearchStatus.Text = "Digite pelo menos 2 letras do nome da cidade.";
                return;
            }

            SearchStatus.Text = "Buscando…";
            SearchSpin.Visibility = Visibility.Visible;
            SearchSpin.IsActive = true;
            try
            {
                var results = await WeatherService.SearchCityAsync(q);
                CityResults.ItemsSource = results;
                CityResults.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                SearchStatus.Text = results.Count == 0
                    ? "Nenhuma cidade com esse nome. Tente sem acentos ou só o nome principal."
                    : "Escolha a cidade na lista:";
            }
            catch (Exception ex)
            {
                SettingsService.Log(ex);
                SearchStatus.Text = "Sem conexão com o serviço de clima. Verifique a internet e tente de novo.";
            }
            finally
            {
                SearchSpin.IsActive = false;
                SearchSpin.Visibility = Visibility.Collapsed;
            }
        }

        private void CityResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(CityResults.SelectedItem is GeoResult g)) return;
            S.City = g.Name;
            S.Latitude = g.Latitude;
            S.Longitude = g.Longitude;
            CurrentCity.Text = "Cidade atual: " + g.Display;
            CityResults.Visibility = Visibility.Collapsed;
            SearchStatus.Text = "";
            CityBox.Clear();
            SettingsService.SaveSoon();
            Weather?.Reload();
        }

        private void Unit_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            S.TemperatureUnit = TagOf(UnitBox) ?? "celsius";
            SettingsService.SaveSoon();
            Weather?.Reload();
            Apply("hardware");
        }

        private void WeatherBg_Click(object sender, RoutedEventArgs e)
        {
            S.WeatherTransparentTile = WeatherBgCheck.IsChecked == true;
            Apply("weather");
        }

        // ================== Música ==================

        private void Music_Click(object sender, RoutedEventArgs e)
        {
            S.MusicShowArt = MusicArtCheck.IsChecked == true;
            S.MusicArtBackground = MusicBackdropCheck.IsChecked == true;
            S.MusicShowProgress = MusicProgressCheck.IsChecked == true;
            Apply("music");
        }

        // ================== Pomodoro ==================

        private void Pomodoro_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            S.PomodoroFocus = (int)FocusSlider.Value;
            S.PomodoroShort = (int)ShortSlider.Value;
            S.PomodoroLong = (int)LongSlider.Value;
            S.PomodoroCycles = (int)CyclesSlider.Value;
            UpdateValueLabels();
            Apply("pomodoro");
        }

        private void PomodoroOptions_Click(object sender, RoutedEventArgs e)
        {
            S.PomodoroAutoStart = PomoAutoCheck.IsChecked == true;
            S.PomodoroSound = PomoSoundCheck.IsChecked == true;
            S.PomodoroNotify = PomoNotifyCheck.IsChecked == true;
            SettingsService.SaveSoon();
        }

        // ================== Componentes ==================

        private void HwOptions_Click(object sender, RoutedEventArgs e)
        {
            S.HwShowCpuTemp = HwCpuTempCheck.IsChecked == true;
            S.HwShowGpuTemp = HwGpuTempCheck.IsChecked == true;
            S.HwShowDiskTemp = HwDiskCheck.IsChecked == true;
            S.HwShowCpuLoad = HwLoadCheck.IsChecked == true;
            S.HwShowRam = HwRamCheck.IsChecked == true;
            Apply("hardware");
        }

        private void Hw_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            S.HardwareRefreshSeconds = (int)HwRefreshSlider.Value;
            S.HardwareWarnTemp = (int)HwWarnSlider.Value;
            UpdateValueLabels();
            Apply("hardware");
        }

        private void UpdateHardwareStatus()
        {
            var s = Hardware?.Last;
            if (s == null)
            {
                HwStatus.Text = S.Layout("hardware").Visible ? "Lendo…" : "Ative o widget Componentes para ver as leituras.";
                HwSources.Text = "";
                return;
            }
            string T(double? v) => v is double d ? $"{d:0} °C" : "indisponível";
            HwStatus.Text = $"CPU: {T(s.CpuTemp)}{(s.CpuTempSource == "aprox." ? " (zona térmica, aproximado)" : "")}   ·   " +
                            $"GPU: {T(s.GpuTemp)}{(string.IsNullOrEmpty(s.GpuName) ? "" : " — " + s.GpuName)}   ·   " +
                            $"SSD: {T(s.DiskTemp)}\nUso da CPU: {s.CpuLoad:0}%   ·   Memória: {s.RamUsedGb:0.0} de {s.RamTotalGb:0} GB ({s.RamPercent:0}%)";
            HwSources.Text = "Fontes: " + HardwareService.LastSources;
        }

        // ================== Todoist ==================

        private void OnTodoistStatus(string status)
        {
            if (!string.IsNullOrEmpty(status)) TodoistStatus.Text = status;
        }

        private void UpdateTodoistUi()
        {
            bool hasToken = !string.IsNullOrEmpty(S.TodoistToken);
            TodoistDisconnect.Visibility = hasToken ? Visibility.Visible : Visibility.Collapsed;

            if (S.NotesSource == "todoist" && !string.IsNullOrEmpty(S.TodoistProjectId))
                TodoistStatus.Text = !string.IsNullOrEmpty(TodoistSync.LastStatus)
                    ? TodoistSync.LastStatus
                    : $"Conectado à lista “{S.TodoistProjectName}”.";
            else if (hasToken)
                TodoistStatus.Text = "Token salvo. Escolha a lista que vai aparecer no widget.";
            else
                TodoistStatus.Text = "Desconectado: a checklist fica só neste PC.";
        }

        private async Task LoadTodoistProjectsAsync(string token, bool silent)
        {
            try
            {
                var projects = await TodoistService.GetProjectsAsync(token);
                _loading = true;
                TodoistProjectBox.ItemsSource = projects;
                TodoistProjectBox.SelectedItem = projects.FirstOrDefault(p => p.Id == S.TodoistProjectId);
                TodoistProjectBox.IsEnabled = true;
                _loading = false;
            }
            catch (Exception ex)
            {
                _loading = false;
                if (!silent) throw;
                SettingsService.Log(ex);
            }
        }

        private async void TodoistConnect_Click(object sender, RoutedEventArgs e)
        {
            string token = TodoistTokenBox.Password.Trim();
            if (token.Length == 0) token = TodoistSync.Token;
            if (string.IsNullOrEmpty(token))
            {
                TodoistStatus.Text = "Cole o token do Todoist no campo acima.";
                return;
            }

            TodoistStatus.Text = "Conectando…";
            try
            {
                await LoadTodoistProjectsAsync(token, silent: false);
                S.TodoistToken = Secret.Protect(token);
                SettingsService.SaveSoon();
                TodoistTokenBox.Clear();
                TodoistDisconnect.Visibility = Visibility.Visible;
                TodoistStatus.Text = S.NotesSource == "todoist"
                    ? $"Conectado à lista “{S.TodoistProjectName}”."
                    : "Conectado! Agora escolha a lista que vai aparecer no widget.";
                if (S.NotesSource == "todoist") TodoistSync.Restart();
            }
            catch (TodoistAuthException)
            {
                TodoistStatus.Text = "Token inválido. Copie de novo em Configurações → Integrações → Desenvolvedor.";
            }
            catch (Exception ex)
            {
                SettingsService.Log(ex);
                TodoistStatus.Text = "Sem conexão com o Todoist. Verifique a internet e tente de novo.";
            }
        }

        private void TodoistProject_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (!(TodoistProjectBox.SelectedItem is TodoistProject p)) return;
            if (S.NotesSource == "todoist" && p.Id == S.TodoistProjectId) return;

            var notes = SettingsService.Notes;
            bool wasTodoist = S.NotesSource == "todoist";

            if (!wasTodoist && notes.Count > 0)
            {
                var r = MessageBox.Show(this,
                    $"Você tem {notes.Count} nota(s) no widget.\n\n" +
                    $"Sim: enviar para a lista “{p.Display}” do Todoist.\n" +
                    "Não: guardar uma cópia de segurança e mostrar só as tarefas do Todoist.",
                    "Conectar ao Todoist", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (r == MessageBoxResult.Cancel)
                {
                    _loading = true;
                    TodoistProjectBox.SelectedItem = null;
                    _loading = false;
                    return;
                }
                if (r == MessageBoxResult.No)
                {
                    SettingsService.BackupNotes();
                    TodoistSync.ClearLocal();
                }
                // Sim: as notas sem id do Todoist são enviadas como tarefas novas
            }
            else if (wasTodoist)
            {
                TodoistSync.ClearLocal(); // trocou de lista: tira as tarefas da lista anterior (sem apagar no Todoist)
            }

            S.NotesSource = "todoist";
            S.TodoistProjectId = p.Id;
            S.TodoistProjectName = p.Display;
            SettingsService.SaveSoon();
            WidgetManager.Apply("notes");
            TodoistStatus.Text = "Sincronizando…";
            TodoistSync.Restart();
        }

        private void TodoistDisconnect_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(this,
                "Desconectar do Todoist?\n\nAs tarefas que estão no widget continuam aqui como notas locais. Nada é apagado no Todoist.",
                "Desconectar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            TodoistSync.Detach();
            S.NotesSource = "local";
            S.TodoistToken = "";
            S.TodoistProjectId = "";
            S.TodoistProjectName = "";
            SettingsService.SaveSoon();
            TodoistSync.Restart();
            WidgetManager.Apply("notes");

            _loading = true;
            TodoistProjectBox.ItemsSource = null;
            TodoistProjectBox.IsEnabled = false;
            _loading = false;
            UpdateTodoistUi();
        }

        // ================== Imagem ==================

        private void PickImage_Click(object sender, RoutedEventArgs e) => ImageW?.PickImage();
        private void RemoveImage_Click(object sender, RoutedEventArgs e) => ImageW?.ClearImage();

        private void PixelArt_Click(object sender, RoutedEventArgs e)
        {
            S.ImagePixelArt = PixelArtCheck.IsChecked == true;
            Apply("image");
        }

        // ================== Dispositivos ==================

        private void BuildDevices()
        {
            DevicesPanel.Children.Clear();
            var devices = Battery?.LastDevices ?? (IReadOnlyList<DeviceBattery>)new List<DeviceBattery>();
            if (devices.Count == 0)
            {
                DevicesPanel.Children.Add(new TextBlock
                {
                    Text = "Nenhum dispositivo com bateria encontrado ainda. Conecte o fone/mouse/controle e clique em procurar.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)FindResource("Muted")
                });
                return;
            }

            foreach (var d in devices)
            {
                var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
                var cb = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = !S.HiddenDevices.Contains(d.Key), Tag = d.Key };
                cb.Click += Device_Click;
                DockPanel.SetDock(cb, Dock.Right);
                row.Children.Add(cb);
                var glyph = new TextBlock
                {
                    Text = d.Glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 16, Width = 28,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(glyph, Dock.Left);
                row.Children.Add(glyph);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = d.Name, Style = (Style)FindResource("RowTitle") });
                text.Children.Add(new TextBlock { Text = d.Level < 0 ? "aguardando leitura" : d.Level + "%", Style = (Style)FindResource("RowDesc") });
                row.Children.Add(text);
                DevicesPanel.Children.Add(row);
            }
        }

        private void Device_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox cb) || !(cb.Tag is string key)) return;
            if (cb.IsChecked == true) S.HiddenDevices.Remove(key);
            else if (!S.HiddenDevices.Contains(key)) S.HiddenDevices.Add(key);
            Battery?.Render();
            SettingsService.SaveSoon();
        }

        private void RefreshDevices_Click(object sender, RoutedEventArgs e)
        {
            if (!S.Layout("battery").Visible) WidgetManager.SetVisible("battery", true);
            Battery?.Refresh();
        }

        private async void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            DiagStatus.Text = "Gerando…";
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("DeskWidgets — diagnóstico de dispositivos");
                sb.AppendLine("Detectados com bateria:");
                foreach (var d in Battery?.LastDevices ?? new List<DeviceBattery>())
                    sb.AppendLine($"  {d.Name} [{d.Source}] {(d.Level < 0 ? "aguardando" : d.Level + "%")}");
                sb.AppendLine();
                sb.Append(await VendorHidBattery.DiagnosticsAsync());
                Clipboard.SetText(sb.ToString());
                DiagStatus.Text = "Copiado. Cole numa issue para adicionar o dispositivo que faltar.";
            }
            catch (Exception ex)
            {
                SettingsService.Log(ex);
                DiagStatus.Text = "Não foi possível gerar o diagnóstico.";
            }
        }

        // ================== Geral ==================

        private void Startup_Click(object sender, RoutedEventArgs e)
        {
            bool want = StartupCheck.IsChecked == true;
            if (StartupService.Set(want))
            {
                S.StartWithWindows = want;
                SettingsService.SaveSoon();
            }
            else
            {
                StartupCheck.IsChecked = !want;
                MessageBox.Show(this, "Não foi possível alterar a inicialização automática. Detalhes em %AppData%\\DeskWidgets\\erros.log.",
                    "DeskWidgets", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void StartMenu_Click(object sender, RoutedEventArgs e)
        {
            bool want = StartMenuCheck.IsChecked == true;
            if (ShortcutService.Set(want))
            {
                S.StartMenuShortcut = want;
                SettingsService.SaveSoon();
            }
            else
            {
                StartMenuCheck.IsChecked = !want;
                MessageBox.Show(this, "Não foi possível alterar o atalho do menu Iniciar. Detalhes em %AppData%\\DeskWidgets\\erros.log.",
                    "DeskWidgets", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(this, "Voltar cores, fontes e estilo ao padrão? Notas, cidade, imagem, layout e temas salvos continuam.",
                "Restaurar aparência", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            SettingsService.ResetAppearance();
            LoadFromSettings();
            WidgetManager.ApplyAll();
            Weather?.Reload();
        }

        private void OpenData_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(SettingsService.Folder) { UseShellExecute = true }); } catch { }
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => App.Instance.ExitApp();
    }
}
