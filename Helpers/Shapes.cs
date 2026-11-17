using System;
using System.Windows;
using System.Windows.Media;

namespace DeskWidgets.Helpers
{
    internal static class ShapeUtil
    {
        /// <summary>Arco a partir do topo, no sentido horário, cobrindo <paramref name="fraction"/> (0..1) do círculo.</summary>
        public static Geometry Arc(Point c, double r, double fraction)
        {
            fraction = Math.Clamp(fraction, 0, 1);
            if (fraction >= 0.9999) return new EllipseGeometry(c, r, r);
            if (fraction <= 0.0001) return Geometry.Empty;

            double a = fraction * 2 * Math.PI;
            var start = new Point(c.X, c.Y - r);
            var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
            var fig = new PathFigure { StartPoint = start, IsClosed = false };
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            g.Freeze();
            return g;
        }

        public static Brush LevelBrush(int level)
        {
            Color c = level > 50 ? Color.FromRgb(0x5B, 0xD1, 0x6A)
                    : level > 20 ? Color.FromRgb(0xFF, 0xC9, 0x3C)
                    : Color.FromRgb(0xFF, 0x5A, 0x5F);
            return ColorUtil.Solid(c);
        }

        /// <summary>Verde → amarelo → vermelho conforme a temperatura se aproxima do limite de alerta.</summary>
        public static Brush TempBrush(double temp, double warn)
        {
            Color c = temp >= warn ? Color.FromRgb(0xFF, 0x5A, 0x5F)
                    : temp >= warn - 15 ? Color.FromRgb(0xFF, 0xC9, 0x3C)
                    : Color.FromRgb(0x5B, 0xD1, 0x6A);
            return ColorUtil.Solid(c);
        }
    }
}
