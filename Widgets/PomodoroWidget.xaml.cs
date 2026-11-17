using System;
using System.Collections.Generic;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeskWidgets.Helpers;

namespace DeskWidgets.Widgets
{
    public partial class PomodoroWidget : WidgetBase
    {
        public override string Id => "pomodoro";
        public override string DisplayName => "Pomodoro";
        public override string Glyph => "";
        public override Size BaseSize => new Size(210, 240);
        public override Size MinSize => new Size(130, 150);

        private enum Mode { Focus = 0, Short = 1, Long = 2 }

        private readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Normal);
        private Mode _mode = Mode.Focus;
        private TimeSpan _remaining;
        private DateTime _end;
        private bool _running;
        private int _completedFocus; // focos concluídos no ciclo atual

        public PomodoroWidget()
        {
            InitializeComponent();
            _timer.Tick += (s, e) => Tick();
            _remaining = Length(_mode);
        }

        private TimeSpan Length(Mode m) => TimeSpan.FromMinutes(Math.Max(1, m switch
        {
            Mode.Short => S.PomodoroShort,
            Mode.Long => S.PomodoroLong,
            _ => S.PomodoroFocus
        }));

        private static string Label(Mode m) => m switch
        {
            Mode.Short => "Pausa curta",
            Mode.Long => "Pausa longa",
            _ => "Foco"
        };

        public override void ApplySettings()
        {
            // Durações mudaram: se estiver parado no início, acompanha o novo valor
            if (!_running && (_atStart || _remaining > Length(_mode))) _remaining = Length(_mode);
            Render();
        }

        private bool _atStart = true;

        public override IEnumerable<MenuItem> MenuItems()
        {
            yield return Item(_running ? "Pausar" : "Iniciar", (s, e) => Toggle());
            yield return Item("Reiniciar", (s, e) => Reset());
            yield return Item("Pular etapa", (s, e) => Complete(skipped: true));
            yield return Item("Zerar ciclos", (s, e) => { _completedFocus = 0; Render(); });
        }

        // ================== Estado ==================

        private void Toggle()
        {
            if (_running) Pause(); else Start();
        }

        private void Start()
        {
            if (_remaining <= TimeSpan.Zero) _remaining = Length(_mode);
            _end = DateTime.Now + _remaining;
            _running = true;
            _atStart = false;
            ScheduleTick();
            Render();
        }

        private void Pause()
        {
            if (!_running) return;
            _remaining = _end - DateTime.Now;
            _running = false;
            _timer.Stop();
            Render();
        }

        private void Reset()
        {
            _running = false;
            _timer.Stop();
            _remaining = Length(_mode);
            _atStart = true;
            Render();
        }

        private void SetMode(Mode m)
        {
            _mode = m;
            Reset();
        }

        private void ScheduleTick()
        {
            // acorda na virada de cada segundo do tempo restante
            var left = _end - DateTime.Now;
            double ms = left.TotalMilliseconds % 1000;
            if (ms < 30) ms += 1000;
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(ms + 5);
            _timer.Start();
        }

        private void Tick()
        {
            _timer.Stop();
            if (!_running) return;
            _remaining = _end - DateTime.Now;
            if (_remaining <= TimeSpan.FromMilliseconds(50))
            {
                Complete(skipped: false);
                return;
            }
            Render();
            ScheduleTick();
        }

        private void Complete(bool skipped)
        {
            var finished = _mode;
            _running = false;
            _timer.Stop();

            Mode next;
            if (finished == Mode.Focus)
            {
                if (!skipped) _completedFocus++;
                int cycles = Math.Max(1, S.PomodoroCycles);
                next = _completedFocus > 0 && _completedFocus % cycles == 0 ? Mode.Long : Mode.Short;
            }
            else
            {
                if (finished == Mode.Long) _completedFocus = 0;
                next = Mode.Focus;
            }

            _mode = next;
            _remaining = Length(next);
            _atStart = true;

            if (!skipped)
            {
                if (S.PomodoroSound) { try { SystemSounds.Exclamation.Play(); } catch { } }
                if (S.PomodoroNotify)
                    App.Instance.Notify(finished == Mode.Focus ? "Foco concluído! 🎉" : "Pausa encerrada",
                        finished == Mode.Focus ? $"Hora de uma {Label(next).ToLowerInvariant()} de {Length(next).TotalMinutes:0} min."
                                               : $"Bora focar por {Length(next).TotalMinutes:0} min.");
                Flash();
            }

            if (S.PomodoroAutoStart && !skipped) Start();
            else Render();
        }

        /// <summary>Pisca o anel ao terminar uma etapa.</summary>
        private void Flash()
        {
            var a = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(280)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) };
            Timeline.SetDesiredFrameRate(a, 30);
            Arc.BeginAnimation(OpacityProperty, a);
            Track.BeginAnimation(OpacityProperty, a);
        }

        // ================== Interface ==================

        private void Render()
        {
            var shown = _running ? _end - DateTime.Now : _remaining;
            if (shown < TimeSpan.Zero) shown = TimeSpan.Zero;
            int secs = (int)Math.Ceiling(shown.TotalSeconds - 0.001);
            TimeText.Text = $"{secs / 60:00}:{secs % 60:00}";

            double total = Length(_mode).TotalSeconds;
            double frac = total <= 0 ? 0 : 1 - Math.Clamp(shown.TotalSeconds / total, 0, 1);
            Arc.Data = ShapeUtil.Arc(new Point(65, 65), 61.5, _atStart && !_running ? 0 : frac);

            ModeText.Text = Label(_mode) + (_running ? "" : _atStart ? "" : " · pausado");
            PlayButton.Content = _running ? "" : "";
            Glass.IsActive = _running;
            Glass.Kind = _mode == Mode.Focus ? Controls.IconKind.Hourglass : Controls.IconKind.Ball;

            ModeFocus.IsChecked = _mode == Mode.Focus;
            ModeShort.IsChecked = _mode == Mode.Short;
            ModeLong.IsChecked = _mode == Mode.Long;

            CycleDots.Children.Clear();
            int cycles = Math.Clamp(S.PomodoroCycles, 1, 8);
            int done = _completedFocus % cycles;
            if (_completedFocus > 0 && done == 0 && _mode == Mode.Long) done = cycles;
            for (int i = 0; i < cycles; i++)
            {
                var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(2, 0, 2, 0) };
                dot.SetResourceReference(Shape.FillProperty, i < done ? "AccentBrush" : "FaintBrush");
                CycleDots.Children.Add(dot);
            }

            if (Host != null)
                Host.Title = _running ? $"{TimeText.Text} · {Label(_mode)}" : "DeskWidgets · Pomodoro";
        }

        private void Play_Click(object sender, RoutedEventArgs e) => Toggle();
        private void Reset_Click(object sender, RoutedEventArgs e) => Reset();
        private void Skip_Click(object sender, RoutedEventArgs e) => Complete(skipped: true);

        private void Mode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && int.TryParse(fe.Tag as string, out int m))
            {
                if (_running && (Mode)m == _mode) return;
                SetMode((Mode)m);
            }
        }
    }
}
