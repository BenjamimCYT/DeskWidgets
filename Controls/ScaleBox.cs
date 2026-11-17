using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeskWidgets.Controls
{
    /// <summary>
    /// Redimensionamento "inteligente": o conteúdo é ampliado pelo lado menor (fontes e ícones crescem junto)
    /// e o lado que sobrar vira espaço extra de layout (mais notas, calendário mais largo etc.).
    /// </summary>
    public class ScaleBox : Decorator
    {
        public static readonly DependencyProperty BaseSizeProperty = DependencyProperty.Register(
            nameof(BaseSize), typeof(Size), typeof(ScaleBox),
            new FrameworkPropertyMetadata(new Size(200, 150), FrameworkPropertyMetadataOptions.AffectsMeasure));

        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);

        public Size BaseSize
        {
            get => (Size)GetValue(BaseSizeProperty);
            set => SetValue(BaseSizeProperty, value);
        }

        public double CurrentScale => _scale.ScaleX;

        private double ScaleFor(Size avail)
        {
            var b = BaseSize;
            if (b.Width <= 0 || b.Height <= 0) return 1;
            double sx = double.IsInfinity(avail.Width) ? double.PositiveInfinity : avail.Width / b.Width;
            double sy = double.IsInfinity(avail.Height) ? double.PositiveInfinity : avail.Height / b.Height;
            double s = Math.Min(sx, sy);
            if (double.IsInfinity(s) || s <= 0) s = 1;
            return Math.Clamp(s, 0.3, 6);
        }

        protected override Size MeasureOverride(Size constraint)
        {
            var child = Child;
            if (child == null) return new Size();
            double s = ScaleFor(constraint);
            var inner = new Size(
                double.IsInfinity(constraint.Width) ? BaseSize.Width : constraint.Width / s,
                double.IsInfinity(constraint.Height) ? BaseSize.Height : constraint.Height / s);
            child.Measure(inner);
            return new Size(
                double.IsInfinity(constraint.Width) ? BaseSize.Width * s : constraint.Width,
                double.IsInfinity(constraint.Height) ? BaseSize.Height * s : constraint.Height);
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            var child = Child;
            if (child == null) return arrangeSize;
            double s = ScaleFor(arrangeSize);
            if (!ReferenceEquals(child.RenderTransform, _scale)) child.RenderTransform = _scale;
            _scale.ScaleX = s;
            _scale.ScaleY = s;
            child.Arrange(new Rect(0, 0, arrangeSize.Width / s, arrangeSize.Height / s));
            return arrangeSize;
        }
    }
}
