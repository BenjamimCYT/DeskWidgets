using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using DeskWidgets.Controls;
using DeskWidgets.Models;
using DeskWidgets.Services;

namespace DeskWidgets.Widgets
{
    /// <summary>Cria as janelas dos widgets, guarda o layout e cuida de encaixe, organização e temas.</summary>
    public static class WidgetManager
    {
        private static AppSettings S => SettingsService.Current;

        public static List<WidgetHost> Hosts { get; } = new List<WidgetHost>();
        public static event Action LayoutChanged;
        /// <summary>Widgets que deram erro ao carregar (o resto continua funcionando).</summary>
        public static List<string> FailedWidgets { get; } = new List<string>();

        private static bool _editMode;

        /// <summary>Ordem padrão (também é a ordem do "alinhar automaticamente").</summary>
        private static readonly (string id, double x, double y)[] DefaultSlots =
        {
            // coluna da esquerda (320 de largura)   coluna da direita (210 de largura)
            ("weather",  0,   0),                    ("notes",    330, 0),
            ("image",    200, 0),                    ("battery",  330, 240),
            ("clock",    0,   110),                  ("pomodoro", 330, 360),
            ("calendar", 0,   310),                  ("hardware", 330, 610),
            ("music",    0,   590),
        };

        public static void Init()
        {
            // Cada widget é criado isolado: se um falhar, os outros abrem normalmente
            var factories = new (string name, Func<WidgetBase> make)[]
            {
                ("Relógio", () => new ClockWidget()), ("Calendário", () => new CalendarWidget()),
                ("Clima", () => new WeatherWidget()), ("Notas", () => new NotesWidget()),
                ("Bateria", () => new BatteryWidget()), ("Imagem", () => new ImageWidget()),
                ("Música", () => new MusicWidget()), ("Pomodoro", () => new PomodoroWidget()),
                ("Componentes", () => new HardwareWidget())
            };

            var widgets = new List<WidgetBase>();
            foreach (var (name, make) in factories)
            {
                try { widgets.Add(make()); }
                catch (Exception ex) { SettingsService.Log(ex); FailedWidgets.Add(name); }
            }

            try { Migrate(widgets.ToArray()); }
            catch (Exception ex) { SettingsService.Log(ex); }
            AnimatedIcon.Enabled = S.AnimatedIcons;

            foreach (var w in widgets)
            {
                WidgetHost host = null;
                try
                {
                    host = new WidgetHost(w);
                    host.LoadLayout();
                    EnsureOnScreen(host);
                    host.ApplySettings();
                    Hosts.Add(host);
                    if (S.Layout(w.Id).Visible) host.Show();
                }
                catch (Exception ex)
                {
                    SettingsService.Log(ex);
                    FailedWidgets.Add(w.DisplayName);
                    try { host?.Close(); } catch { }
                    Hosts.Remove(host);
                }
            }
        }

        public static T Get<T>() where T : WidgetBase => Hosts.Select(h => h.Widget).OfType<T>().FirstOrDefault();
        public static WidgetHost HostOf(string id) => Hosts.FirstOrDefault(h => h.Id == id);
        public static IEnumerable<WidgetBase> All => Hosts.Select(h => h.Widget);

        // ================== Migração da v1 ==================

        private static void Migrate(WidgetBase[] widgets)
        {
            if (S.LayoutVersion >= 2) { FillMissing(widgets); return; }

            var anchor = DefaultAnchor();
            var screen = VirtualScreen();
            if (S.Left > -99990 && screen.Contains(new Point(S.Left + 40, S.Top + 40)))
                anchor = new Point(S.Left, S.Top);

            var oldVisible = new Dictionary<string, bool>
            {
                ["clock"] = S.ShowClock, ["calendar"] = S.ShowCalendar, ["weather"] = S.ShowWeather,
                ["notes"] = S.ShowNotes, ["battery"] = S.ShowBattery, ["image"] = S.ShowImage
            };

            foreach (var w in widgets)
            {
                var l = S.Layout(w.Id);
                var slot = DefaultSlots.FirstOrDefault(d => d.id == w.Id);
                l.Left = anchor.X + slot.x;
                l.Top = anchor.Y + slot.y;
                l.Width = w.DefaultSize.Width;
                l.Height = w.DefaultSize.Height;
                l.Visible = !oldVisible.TryGetValue(w.Id, out bool v) || v;
            }
            S.CornerRadius = Math.Max(S.CornerRadius, 12);
            S.LayoutVersion = 2;
            SettingsService.SaveSoon();
        }

        private static void FillMissing(WidgetBase[] widgets)
        {
            var anchor = DefaultAnchor();
            foreach (var w in widgets)
            {
                var l = S.Layout(w.Id);
                if (l.HasPosition) continue;
                var slot = DefaultSlots.FirstOrDefault(d => d.id == w.Id);
                l.Left = anchor.X + slot.x;
                l.Top = anchor.Y + slot.y;
            }
        }

        private static Point DefaultAnchor()
        {
            var wa = SystemParameters.WorkArea;
            return new Point(Math.Max(wa.Left + 20, wa.Right - 570), wa.Top + 30);
        }

        private static Rect VirtualScreen() => new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                                        SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        private static void EnsureOnScreen(WidgetHost h)
        {
            var screen = VirtualScreen();
            if (screen.Contains(new Point(h.Left + 30, h.Top + 30))) return;
            var anchor = DefaultAnchor();
            var slot = DefaultSlots.FirstOrDefault(d => d.id == h.Id);
            h.Left = anchor.X + slot.x;
            h.Top = anchor.Y + slot.y;
            h.SaveLayout();
        }

        // ================== Visibilidade / aparência ==================

        public static void ApplyAll()
        {
            AnimatedIcon.Enabled = S.AnimatedIcons;
            foreach (var h in Hosts) h.ApplySettings();
        }

        public static void Apply(string id) => HostOf(id)?.ApplySettings();

        public static void SetVisible(string id, bool visible)
        {
            var h = HostOf(id);
            if (h == null) return;
            S.Layout(id).Visible = visible;
            if (visible)
            {
                EnsureOnScreen(h);
                h.Show();
                h.ApplyZOrder();
            }
            else h.Hide();
            SettingsService.SaveSoon();
            LayoutChanged?.Invoke();
        }

        public static void SetWidgetTheme(string id, string theme)
        {
            S.Layout(id).Theme = theme ?? "";
            Apply(id);
            SettingsService.SaveSoon();
            LayoutChanged?.Invoke();
        }

        public static void SetLocked(bool locked)
        {
            S.Locked = locked;
            if (locked) EditMode = false;
            foreach (var h in Hosts) h.UpdateChrome();
            SettingsService.SaveSoon();
            LayoutChanged?.Invoke();
        }

        /// <summary>Modo de edição: contornos, nomes e alças visíveis; widgets ficam por cima para facilitar.</summary>
        public static bool EditMode
        {
            get => _editMode;
            set
            {
                if (_editMode == value) return;
                _editMode = value;
                if (value && S.Locked) { S.Locked = false; SettingsService.SaveSoon(); }
                foreach (var h in Hosts)
                {
                    h.UpdateChrome();
                    h.ApplyZOrder();
                }
                LayoutChanged?.Invoke();
            }
        }

        public static void BringBackAll()
        {
            foreach (var h in Hosts.Where(h => S.Layout(h.Id).Visible))
            {
                if (!h.IsVisible) h.Show();
                h.BringToFront(activate: false);
            }
        }

        public static void RefreshAll()
        {
            foreach (var w in All) w.Refresh();
        }

        // ================== Encaixe ==================

        private const double Magnet = 12;

        private static IEnumerable<Rect> Others(WidgetHost self) =>
            Hosts.Where(h => h != self && h.IsVisible).Select(h => h.Bounds);

        private static Rect WorkAreaFor(WidgetHost h, double left, double top)
        {
            try
            {
                double d = System.Windows.Media.VisualTreeHelper.GetDpi(h).DpiScaleX;
                var scr = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)((left + 20) * d), (int)((top + 20) * d)));
                var wa = scr.WorkingArea;
                return new Rect(wa.Left / d, wa.Top / d, wa.Width / d, wa.Height / d);
            }
            catch { return SystemParameters.WorkArea; }
        }

        /// <summary>Encaixa a posição nas bordas de outros widgets e da tela; senão, na grade.</summary>
        public static Point SnapPosition(WidgetHost self, double l, double t, double w, double h)
        {
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) != 0)
                return new Point(Math.Round(l), Math.Round(t)); // Alt = mover livre

            double gap = Math.Clamp(S.WidgetGap, 0, 60);
            double bestX = double.NaN, bestY = double.NaN, dX = Magnet, dY = Magnet;

            void TryX(double cand) { double d = Math.Abs(cand - l); if (d < dX) { dX = d; bestX = cand; } }
            void TryY(double cand) { double d = Math.Abs(cand - t); if (d < dY) { dY = d; bestY = cand; } }

            if (S.SnapToWidgets)
            {
                foreach (var o in Others(self))
                {
                    bool nearY = t < o.Bottom + gap + Magnet && t + h > o.Top - gap - Magnet;
                    bool nearX = l < o.Right + gap + Magnet && l + w > o.Left - gap - Magnet;
                    if (nearY)
                    {
                        TryX(o.Right + gap); TryX(o.Left - gap - w); TryX(o.Left); TryX(o.Right - w);
                    }
                    if (nearX)
                    {
                        TryY(o.Bottom + gap); TryY(o.Top - gap - h); TryY(o.Top); TryY(o.Bottom - h);
                    }
                }
                var wa = WorkAreaFor(self, l, t);
                TryX(wa.Left + gap); TryX(wa.Right - gap - w);
                TryY(wa.Top + gap); TryY(wa.Bottom - gap - h);
            }

            double grid = Math.Max(2, S.GridSize);
            double x = !double.IsNaN(bestX) ? bestX : S.SnapToGrid ? Math.Round(l / grid) * grid : l;
            double y = !double.IsNaN(bestY) ? bestY : S.SnapToGrid ? Math.Round(t / grid) * grid : t;
            return new Point(Math.Round(x), Math.Round(y));
        }

        /// <summary>Encaixa a borda direita/inferior ao redimensionar.</summary>
        public static Size SnapSize(WidgetHost self, double w, double h)
        {
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) != 0)
                return new Size(Math.Round(w), Math.Round(h));

            double l = self.Left, t = self.Top;
            double r = l + w, b = t + h;
            double gap = Math.Clamp(S.WidgetGap, 0, 60);
            double bestR = double.NaN, bestB = double.NaN, dR = Magnet, dB = Magnet;

            void TryR(double cand) { double d = Math.Abs(cand - r); if (d < dR) { dR = d; bestR = cand; } }
            void TryB(double cand) { double d = Math.Abs(cand - b); if (d < dB) { dB = d; bestB = cand; } }

            if (S.SnapToWidgets)
            {
                foreach (var o in Others(self))
                {
                    bool nearY = t < o.Bottom + Magnet && b > o.Top - Magnet;
                    bool nearX = l < o.Right + Magnet && r > o.Left - Magnet;
                    if (nearY) { TryR(o.Left - gap); TryR(o.Right); }
                    if (nearX) { TryB(o.Top - gap); TryB(o.Bottom); }
                }
            }

            double grid = Math.Max(2, S.GridSize);
            double nr = !double.IsNaN(bestR) ? bestR : S.SnapToGrid ? Math.Round(r / grid) * grid : r;
            double nb = !double.IsNaN(bestB) ? bestB : S.SnapToGrid ? Math.Round(b / grid) * grid : b;
            return new Size(Math.Round(nr - l), Math.Round(nb - t));
        }

        // ================== Organizar ==================

        /// <summary>Empilha os widgets visíveis em colunas, da direita para a esquerda da tela principal.</summary>
        public static void AutoArrange()
        {
            var wa = SystemParameters.WorkArea;
            double gap = Math.Max(6, S.WidgetGap);
            var order = DefaultSlots.Select(d => d.id).ToList();
            var visible = Hosts.Where(h => h.IsVisible).OrderBy(h => order.IndexOf(h.Id)).ToList();

            double right = wa.Right - gap - 10;
            double y = wa.Top + gap + 10;
            double colWidth = 0;
            var column = new List<WidgetHost>();

            void FlushColumn()
            {
                foreach (var h in column) h.MoveTo(right - colWidth, h.Top);
                right -= colWidth + gap;
                column.Clear();
                colWidth = 0;
                y = wa.Top + gap + 10;
            }

            foreach (var h in visible)
            {
                if (column.Count > 0 && y + h.Height > wa.Bottom - gap) FlushColumn();
                h.Top = y;
                column.Add(h);
                colWidth = Math.Max(colWidth, h.Width);
                y += h.Height + gap;
            }
            if (column.Count > 0) FlushColumn();

            SettingsService.SaveSoon();
            LayoutChanged?.Invoke();
        }

        /// <summary>Volta posições e tamanhos ao padrão (temas por widget e visibilidade continuam).</summary>
        public static void ResetLayout()
        {
            var anchor = DefaultAnchor();
            foreach (var h in Hosts)
            {
                var slot = DefaultSlots.FirstOrDefault(d => d.id == h.Id);
                h.MoveTo(anchor.X + slot.x, anchor.Y + slot.y, h.Widget.DefaultSize.Width, h.Widget.DefaultSize.Height);
            }
            LayoutChanged?.Invoke();
        }

        public static void SaveAll()
        {
            foreach (var h in Hosts) if (h.IsLoaded) h.SaveLayout();
        }
    }
}
