using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using DeskWidgets.Helpers;
using DeskWidgets.Models;
using DeskWidgets.Services;

namespace DeskWidgets
{
    public partial class SettingsWindow : Window
    {
        private readonly MainWindow _main;
        private bool _loading = true; // evita aplicar mudanças enquanto os controles são preenchidos

        private static AppSettings S => SettingsService.Current;

        public SettingsWindow(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            FontBox.ItemsSource = Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            LoadFromSettings();
            if (!string.IsNullOrEmpty(TodoistSync.Token)) _ = LoadTodoistProjectsAsync(TodoistSync.Token, silent: true);

            _main.DevicesUpdated += OnDevicesUpdated;
            TodoistSync.StatusChanged += OnTodoistStatus;
            Closed += (s, e) =>
            {
                _main.DevicesUpdated -= OnDevicesUpdated;
                TodoistSync.StatusChanged -= OnTodoistStatus;
                SettingsService.SaveNow();
                ResourceSaver.Trim(); // libera a lista de fontes etc.
            };
        }

        private void LoadFromSettings()
        {
            _loading = true;

            BuildPalettes();
            TopHex.Text = S.GradientTop;
            BottomHex.Text = S.GradientBottom;
            AccentHex.Text = S.Accent;
            TextHex.Text = S.TextColor;
            UpdateSwatches();

            SelectByTag(DirectionBox, S.GradientDirection);
            OpacitySlider.Value = Math.Round(S.TileOpacity * 100);
            RadiusSlider.Value = S.CornerRadius;
            ScaleSlider.Value = Math.Round(S.Scale * 100);

            SelectByTag(ZModeBox, S.ZMode);
            LockCheck.IsChecked = S.Locked;
            StartupCheck.IsChecked = StartupService.IsEnabled();
            StartMenuCheck.IsChecked = S.StartMenuShortcut && ShortcutService.Exists();

            FontBox.SelectedItem = (FontBox.ItemsSource as System.Collections.Generic.List<string>)?
                .FirstOrDefault(f => string.Equals(f, S.ClockFont, StringComparison.OrdinalIgnoreCase));
            SetPreviewFont();
            ClockSizeSlider.Value = S.ClockSize;
            BoldCheck.IsChecked = S.ClockBold;
            H24Check.IsChecked = S.Clock24h;
            SecondsCheck.IsChecked = S.ShowSeconds;
            SelectByTag(CultureBox, S.DateCulture);

            CurrentCity.Text = "Cidade atual: " + S.City;
            SelectByTag(UnitBox, S.TemperatureUnit);
            WeatherBgCheck.IsChecked = S.WeatherTransparentTile;

            PixelArtCheck.IsChecked = S.ImagePixelArt;
            UpdateTodoistUi();

            ShowClockCheck.IsChecked = S.ShowClock;
            ShowCalendarCheck.IsChecked = S.ShowCalendar;
            ShowNotesCheck.IsChecked = S.ShowNotes;
            ShowWeatherCheck.IsChecked = S.ShowWeather;
            ShowBatteryCheck.IsChecked = S.ShowBattery;
            ShowImageCheck.IsChecked = S.ShowImage;

            BuildDevices();
            UpdateValueLabels();
            _loading = false;
        }

        private void Apply()
        {
            if (_loading) return;
            _main.ApplySettings();
            SettingsService.SaveSoon();
        }

        // ---------- helpers ----------

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
            if (OpacityValue != null) OpacityValue.Text = (int)OpacitySlider.Value + "%";
            if (RadiusValue != null) RadiusValue.Text = (int)RadiusSlider.Value + " px";
            if (ScaleValue != null) ScaleValue.Text = (int)ScaleSlider.Value + "%";
            if (ClockSizeValue != null) ClockSizeValue.Text = (int)ClockSizeSlider.Value + " px";
        }

        private void UpdateSwatches()
        {
            TopSwatch.Background = ColorUtil.Solid(ColorUtil.Parse(S.GradientTop, Colors.Black));
            BottomSwatch.Background = ColorUtil.Solid(ColorUtil.Parse(S.GradientBottom, Colors.Black));
            AccentSwatch.Background = ColorUtil.Solid(ColorUtil.Parse(S.Accent, Colors.White));
            TextSwatch.Background = ColorUtil.Solid(ColorUtil.Parse(S.TextColor, Colors.White));
        }

        // ---------- paletas ----------

        private void BuildPalettes()
        {
            PalettePanel.Children.Clear();
            foreach (var p in Palettes.All)
            {
                var top = ColorUtil.Parse(p.Top, Colors.Black);
                var bottom = ColorUtil.Parse(p.Bottom, Colors.Black);
                bool selected = string.Equals(S.PaletteName, p.Name, StringComparison.Ordinal);

                var swatch = new Border
                {
                    Width = 72,
                    Height = 44,
                    CornerRadius = new CornerRadius(8),
                    Background = new LinearGradientBrush(top, bottom, 90),
                    BorderBrush = selected ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    Child = new Border
                    {
                        Width = 10,
                        Height = 10,
                        CornerRadius = new CornerRadius(5),
                        Background = ColorUtil.Solid(ColorUtil.Parse(p.Accent, Colors.White)),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 6, 6)
                    }
                };

                var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };
                stack.Children.Add(swatch);
                stack.Children.Add(new TextBlock
                {
                    Text = p.Name,
                    FontSize = 11.5,
                    Opacity = selected ? 1 : 0.7,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0)
                });

                var btn = new Button { Style = (Style)FindResource("PlainButton"), Content = stack, Tag = p, ToolTip = p.Name };
                btn.Click += Palette_Click;
                PalettePanel.Children.Add(btn);
            }
        }

        private void Palette_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is Palette p)) return;
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
            Apply();
        }

        // ---------- cores personalizadas ----------

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
            Apply();
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
                box.Text = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}"; // dispara Hex_TextChanged
        }

        // ---------- aparência ----------

        private void Direction_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            S.GradientDirection = TagOf(DirectionBox) ?? "Vertical";
            Apply();
        }

        private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            S.TileOpacity = OpacitySlider.Value / 100.0;
            UpdateValueLabels();
            Apply();
        }

        private void Radius_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            S.CornerRadius = RadiusSlider.Value;
            UpdateValueLabels();
            Apply();
        }

        private void Scale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            S.Scale = ScaleSlider.Value / 100.0;
            UpdateValueLabels();
            Apply();
        }

        private void ZMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            S.ZMode = TagOf(ZModeBox) ?? "Desktop";
            Apply();
        }

        private void Lock_Click(object sender, RoutedEventArgs e)
        {
            S.Locked = LockCheck.IsChecked == true;
            SettingsService.SaveSoon();
        }

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

        // ---------- relógio ----------

        private void SetPreviewFont()
        {
            string name = FontBox.SelectedItem as string ?? S.ClockFont;
            try { FontPreview.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(name) ? "Times New Roman" : name); } catch { }
        }

        private void Font_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (FontBox.SelectedItem is string name)
            {
                S.ClockFont = name;
                SetPreviewFont();
                Apply();
            }
        }

        private void ClockSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            S.ClockSize = ClockSizeSlider.Value;
            UpdateValueLabels();
            Apply();
        }

        private void ClockOptions_Click(object sender, RoutedEventArgs e)
        {
            S.ClockBold = BoldCheck.IsChecked == true;
            S.Clock24h = H24Check.IsChecked == true;
            S.ShowSeconds = SecondsCheck.IsChecked == true;
            Apply();
        }

        private void Culture_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            S.DateCulture = TagOf(CultureBox) ?? "pt-BR";
            Apply();
        }

        // ---------- clima ----------

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
            _main.RefreshWeather();
        }

        private void Unit_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            S.TemperatureUnit = TagOf(UnitBox) ?? "celsius";
            SettingsService.SaveSoon();
            _main.RefreshWeather();
        }

        private void WeatherBg_Click(object sender, RoutedEventArgs e)
        {
            S.WeatherTransparentTile = WeatherBgCheck.IsChecked == true;
            Apply();
        }

        // ---------- Todoist ----------

        private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
            e.Handled = true;
        }

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
            _main.ApplySettings();
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
            _main.ApplySettings();

            _loading = true;
            TodoistProjectBox.ItemsSource = null;
            TodoistProjectBox.IsEnabled = false;
            _loading = false;
            UpdateTodoistUi();
        }

        // ---------- imagem ----------

        private void PickImage_Click(object sender, RoutedEventArgs e) => _main.PickImage();
        private void RemoveImage_Click(object sender, RoutedEventArgs e) => _main.ClearImage();

        private void PixelArt_Click(object sender, RoutedEventArgs e)
        {
            S.ImagePixelArt = PixelArtCheck.IsChecked == true;
            Apply();
        }

        // ---------- blocos visíveis ----------

        private void Visibility_Click(object sender, RoutedEventArgs e)
        {
            bool batteryWasOff = !S.ShowBattery;
            bool weatherWasOff = !S.ShowWeather;

            S.ShowClock = ShowClockCheck.IsChecked == true;
            S.ShowCalendar = ShowCalendarCheck.IsChecked == true;
            S.ShowNotes = ShowNotesCheck.IsChecked == true;
            S.ShowWeather = ShowWeatherCheck.IsChecked == true;
            S.ShowBattery = ShowBatteryCheck.IsChecked == true;
            S.ShowImage = ShowImageCheck.IsChecked == true;
            Apply();

            if (batteryWasOff && S.ShowBattery) _main.RefreshBattery();
            if (weatherWasOff && S.ShowWeather) _main.RefreshWeather();
        }

        // ---------- dispositivos ----------

        private void OnDevicesUpdated() => BuildDevices();

        private void BuildDevices()
        {
            DevicesPanel.Children.Clear();
            var devices = _main.LastDevices;
            if (devices.Count == 0)
            {
                DevicesPanel.Children.Add(new TextBlock
                {
                    Text = "Nenhum dispositivo com bateria encontrado ainda. Conecte o fone/mouse/controle e clique em procurar.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7
                });
                return;
            }

            foreach (var d in devices)
            {
                var cb = new CheckBox
                {
                    Content = d.Level < 0 ? $"{d.Name} (aguardando leitura)" : $"{d.Name} ({d.Level}%)",
                    IsChecked = !S.HiddenDevices.Contains(d.Key),
                    Tag = d.Key
                };
                cb.Click += Device_Click;
                DevicesPanel.Children.Add(cb);
            }
        }

        private void Device_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox cb) || !(cb.Tag is string key)) return;
            if (cb.IsChecked == true) S.HiddenDevices.Remove(key);
            else if (!S.HiddenDevices.Contains(key)) S.HiddenDevices.Add(key);
            _main.RenderBatteries();
            SettingsService.SaveSoon();
        }

        private void RefreshDevices_Click(object sender, RoutedEventArgs e) => _main.RefreshBattery();

        private async void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            DiagStatus.Text = "Gerando…";
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("DeskWidgets — diagnóstico de dispositivos");
                sb.AppendLine("Detectados com bateria:");
                foreach (var d in _main.LastDevices)
                    sb.AppendLine($"  {d.Name} [{d.Source}] {(d.Level < 0 ? "aguardando" : d.Level + "%")}");
                sb.AppendLine();
                sb.Append(await VendorHidBattery.DiagnosticsAsync());
                Clipboard.SetText(sb.ToString());
                DiagStatus.Text = "Copiado. Cole na conversa para eu adicionar o dispositivo que faltar.";
            }
            catch (Exception ex)
            {
                SettingsService.Log(ex);
                DiagStatus.Text = "Não foi possível gerar o diagnóstico.";
            }
        }

        // ---------- rodapé ----------

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(this, "Voltar cores, fonte e tamanho ao padrão? Suas notas, cidade e imagem continuam.",
                "Restaurar aparência", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            SettingsService.ResetAppearance();
            LoadFromSettings();
            _main.ApplySettings();
            _main.RefreshWeather();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
