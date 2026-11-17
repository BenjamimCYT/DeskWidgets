using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DeskWidgets.Helpers;

namespace DeskWidgets.Controls
{
    public enum IconKind { Spinner, Dots, Pulse, Ball, Clock, Hourglass, Wifi, Equalizer }

    /// <summary>
    /// Ícones animados vetoriais (círculo, pontos, círculos que somem, bola, tempo, wi-fi, equalizador).
    /// Só animam quando <see cref="IsActive"/> está ligado e o ícone está visível: parado, o custo é zero.
    /// </summary>
    public class AnimatedIcon : Viewbox
    {
        // Liga/desliga todas as animações do app (Configurações → Temas → Ícones animados)
        private static bool _enabled = true;
        private static event Action EnabledChanged;
        public static bool Enabled
        {
            get => _enabled;
            set { if (_enabled == value) return; _enabled = value; EnabledChanged?.Invoke(); }
        }

        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(IconKind), typeof(AnimatedIcon),
            new PropertyMetadata(IconKind.Spinner, (d, e) => ((AnimatedIcon)d).Rebuild()));

        public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
            nameof(IsActive), typeof(bool), typeof(AnimatedIcon),
            new PropertyMetadata(false, (d, e) => ((AnimatedIcon)d).UpdateRunning()));

        public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
            nameof(Brush), typeof(Brush), typeof(AnimatedIcon),
            new PropertyMetadata(Brushes.White, (d, e) => ((AnimatedIcon)d).Rebuild()));

        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
            nameof(Thickness), typeof(double), typeof(AnimatedIcon),
            new PropertyMetadata(2.0, (d, e) => ((AnimatedIcon)d).Rebuild()));

        public IconKind Kind { get => (IconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
        public Brush Brush { get => (Brush)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
        public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

        private readonly Canvas _canvas = new Canvas { Width = 24, Height = 24 };
        private readonly List<(IAnimatable target, DependencyProperty prop, AnimationTimeline anim)> _anims
            = new List<(IAnimatable, DependencyProperty, AnimationTimeline)>();
        private bool _running;

        public AnimatedIcon()
        {
            Child = _canvas;
            Stretch = Stretch.Uniform;
            Width = 18;
            Height = 18;
            IsHitTestVisible = false;
            SetResourceReference(BrushProperty, "TextBrush");
            Loaded += (s, e) => { EnabledChanged += UpdateRunning; UpdateRunning(); };
            Unloaded += (s, e) => { EnabledChanged -= UpdateRunning; Stop(); };
            IsVisibleChanged += (s, e) => UpdateRunning();
            Rebuild();
        }

        private void UpdateRunning()
        {
            bool want = IsActive && Enabled && IsVisible && IsLoaded;
            if (want == _running) return;
            if (want) Start(); else Stop();
        }

        private void Start()
        {
            _running = true;
            foreach (var (t, p, a) in _anims) t.BeginAnimation(p, a);
        }

        private void Stop()
        {
            _running = false;
            foreach (var (t, p, _) in _anims) t.BeginAnimation(p, null);
        }

        private void Rebuild()
        {
            bool was = _running;
            if (was) Stop();
            _canvas.Children.Clear();
            _anims.Clear();

            var b = Brush ?? Brushes.White;
            double th = Thickness;

            switch (Kind)
            {
                case IconKind.Spinner:
                {
                    Add(Circle(12, 12, 9.5, null, b, th, 0.22));
                    var rot = new RotateTransform(0, 12, 12);
                    Add(new Path
                    {
                        Data = ShapeUtil.Arc(new Point(12, 12), 9.5, 0.3),
                        Stroke = b, StrokeThickness = th,
                        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                        RenderTransform = rot
                    });
                    Anim(rot, RotateTransform.AngleProperty, Loop(0, 360, 0.9));
                    break;
                }
                case IconKind.Dots:
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var tr = new TranslateTransform();
                        var dot = Circle(5 + i * 7, 12, 2.4, b, null, 0, 0.45);
                        dot.RenderTransform = tr;
                        Add(dot);
                        var up = new DoubleAnimationUsingKeyFrames { Duration = Sec(1.0), RepeatBehavior = RepeatBehavior.Forever, BeginTime = Sec(i * 0.14) };
                        up.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(Sec(0))));
                        up.KeyFrames.Add(new EasingDoubleKeyFrame(-4, KeyTime.FromTimeSpan(Sec(0.25)), new SineEase()));
                        up.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(Sec(0.5)), new SineEase()));
                        Anim(tr, TranslateTransform.YProperty, up);
                        var op = new DoubleAnimationUsingKeyFrames { Duration = Sec(1.0), RepeatBehavior = RepeatBehavior.Forever, BeginTime = Sec(i * 0.14) };
                        op.KeyFrames.Add(new LinearDoubleKeyFrame(0.45, KeyTime.FromTimeSpan(Sec(0))));
                        op.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(Sec(0.25))));
                        op.KeyFrames.Add(new LinearDoubleKeyFrame(0.45, KeyTime.FromTimeSpan(Sec(0.5))));
                        Anim(dot, UIElement.OpacityProperty, op);
                    }
                    break;
                }
                case IconKind.Pulse:
                {
                    for (int i = 0; i < 2; i++)
                    {
                        var sc = new ScaleTransform(1, 1, 12, 12);
                        var ring = Circle(12, 12, 9.5, null, b, th * 0.8, i == 0 ? 0.6 : 0);
                        ring.RenderTransform = sc;
                        Add(ring);
                        var grow = new DoubleAnimation(0.35, 1, Sec(1.6)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = Sec(i * 0.8), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                        Anim(sc, ScaleTransform.ScaleXProperty, grow);
                        Anim(sc, ScaleTransform.ScaleYProperty, grow);
                        Anim(ring, UIElement.OpacityProperty, new DoubleAnimation(1, 0, Sec(1.6)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = Sec(i * 0.8) });
                    }
                    Add(Circle(12, 12, 3.6, b, null, 0, 1));
                    break;
                }
                case IconKind.Ball:
                {
                    var shScale = new ScaleTransform(1, 1, 12, 21);
                    var shadow = new Ellipse { Width = 9, Height = 2.2, Fill = b, Opacity = 0.3, RenderTransform = shScale };
                    Canvas.SetLeft(shadow, 7.5); Canvas.SetTop(shadow, 19.9);
                    Add(shadow);
                    var tr = new TranslateTransform();
                    var ball = Circle(12, 5.5, 3.2, b, null, 0, 1);
                    ball.RenderTransform = tr;
                    Add(ball);
                    Anim(tr, TranslateTransform.YProperty, new DoubleAnimation(0, 11, Sec(0.42)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
                    Anim(shScale, ScaleTransform.ScaleXProperty, new DoubleAnimation(0.45, 1.15, Sec(0.42)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
                    break;
                }
                case IconKind.Clock:
                {
                    Add(Circle(12, 12, 9.5, null, b, th, 1));
                    var hRot = new RotateTransform(60, 12, 12);
                    var mRot = new RotateTransform(0, 12, 12);
                    Add(Line(12, 12, 12, 7.6, b, th, hRot));
                    Add(Line(12, 12, 12, 5.2, b, th * 0.85, mRot));
                    Add(Circle(12, 12, 1.2, b, null, 0, 1));
                    Anim(mRot, RotateTransform.AngleProperty, Loop(0, 360, 1.6));
                    Anim(hRot, RotateTransform.AngleProperty, Loop(60, 420, 19.2));
                    break;
                }
                case IconKind.Hourglass:
                {
                    var rot = new RotateTransform(0, 12, 12);
                    var g = new Canvas { Width = 24, Height = 24, RenderTransform = rot };
                    g.Children.Add(new Path
                    {
                        Data = Geometry.Parse("M6.5,3 H17.5 M6.5,21 H17.5 M8,3 C8,9 11.2,10 11.2,12 C11.2,14 8,15 8,21 M16,3 C16,9 12.8,10 12.8,12 C12.8,14 16,15 16,21"),
                        Stroke = b, StrokeThickness = th * 0.85, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
                    });
                    var top = new Path { Data = Geometry.Parse("M9.6,6.2 H14.4 L12,10.4 Z"), Fill = b };
                    var bottom = new Path { Data = Geometry.Parse("M9.2,19.6 L12,15.6 L14.8,19.6 Z"), Fill = b, Opacity = 0.35 };
                    g.Children.Add(top);
                    g.Children.Add(bottom);
                    Add(g);
                    var flip = new DoubleAnimationUsingKeyFrames { Duration = Sec(2.0), RepeatBehavior = RepeatBehavior.Forever };
                    flip.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(Sec(0))));
                    flip.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(Sec(1.55))));
                    flip.KeyFrames.Add(new EasingDoubleKeyFrame(180, KeyTime.FromTimeSpan(Sec(2.0)), new BackEase { Amplitude = 0.35 }));
                    Anim(rot, RotateTransform.AngleProperty, flip);
                    Anim(top, UIElement.OpacityProperty, new DoubleAnimation(1, 0.25, Sec(1.55)) { RepeatBehavior = RepeatBehavior.Forever, Duration = Sec(2.0) });
                    Anim(bottom, UIElement.OpacityProperty, new DoubleAnimation(0.35, 1, Sec(1.55)) { RepeatBehavior = RepeatBehavior.Forever, Duration = Sec(2.0) });
                    break;
                }
                case IconKind.Wifi:
                {
                    Add(Circle(12, 19, 1.7, b, null, 0, 1));
                    for (int k = 1; k <= 3; k++)
                    {
                        double r = 4.6 * k;
                        double dx = r * Math.Sin(Math.PI / 4), dy = r * Math.Cos(Math.PI / 4);
                        var fig = new PathFigure { StartPoint = new Point(12 - dx, 19 - dy) };
                        fig.Segments.Add(new ArcSegment(new Point(12 + dx, 19 - dy), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
                        var geo = new PathGeometry(new[] { fig });
                        geo.Freeze();
                        var arc = new Path { Data = geo, Stroke = b, StrokeThickness = th, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                        Add(arc);
                        var op = new DoubleAnimationUsingKeyFrames { Duration = Sec(1.8), RepeatBehavior = RepeatBehavior.Forever };
                        op.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.18, KeyTime.FromTimeSpan(Sec(0))));
                        op.KeyFrames.Add(new LinearDoubleKeyFrame(0.18, KeyTime.FromTimeSpan(Sec(k * 0.28))));
                        op.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(Sec(k * 0.28 + 0.16))));
                        op.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(Sec(1.4))));
                        op.KeyFrames.Add(new LinearDoubleKeyFrame(0.18, KeyTime.FromTimeSpan(Sec(1.75))));
                        Anim(arc, UIElement.OpacityProperty, op);
                    }
                    break;
                }
                case IconKind.Equalizer:
                {
                    double[] rest = { 0.45, 0.8, 0.3, 0.6 };
                    double[] speed = { 0.42, 0.58, 0.36, 0.5 };
                    for (int i = 0; i < 4; i++)
                    {
                        var sc = new ScaleTransform(1, rest[i], 0, 20);
                        var bar = new Rectangle { Width = 3.2, Height = 16, RadiusX = 1.6, RadiusY = 1.6, Fill = b, RenderTransform = sc };
                        Canvas.SetLeft(bar, 3.6 + i * 4.7);
                        Canvas.SetTop(bar, 4);
                        Add(bar);
                        Anim(sc, ScaleTransform.ScaleYProperty, new DoubleAnimation(0.2, 1, Sec(speed[i]))
                        {
                            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                            BeginTime = Sec(i * 0.07), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                        });
                    }
                    break;
                }
            }

            if (was) Start(); else UpdateRunning();
        }

        // ---------- utilitários ----------

        private void Add(UIElement e) => _canvas.Children.Add(e);

        private void Anim(IAnimatable target, DependencyProperty prop, AnimationTimeline a)
        {
            Timeline.SetDesiredFrameRate(a, 30); // metade do padrão: suave e leve
            if (a.CanFreeze) a.Freeze();
            _anims.Add((target, prop, a));
        }

        private static TimeSpan Sec(double s) => TimeSpan.FromSeconds(s);

        private static DoubleAnimation Loop(double from, double to, double seconds)
            => new DoubleAnimation(from, to, Sec(seconds)) { RepeatBehavior = RepeatBehavior.Forever };

        private static Ellipse Circle(double cx, double cy, double r, Brush fill, Brush stroke, double th, double opacity)
        {
            var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = stroke, StrokeThickness = th, Opacity = opacity };
            Canvas.SetLeft(e, cx - r);
            Canvas.SetTop(e, cy - r);
            return e;
        }

        private static Line Line(double x1, double y1, double x2, double y2, Brush b, double th, Transform t)
            => new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = th, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = t };
    }
}
