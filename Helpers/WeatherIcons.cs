using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DeskWidgets.Helpers
{
    public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Storm }

    /// <summary>
    /// Ícones vetoriais (sem arquivos de imagem). Cada combinação é desenhada uma única vez,
    /// congelada (Freeze) e reaproveitada — custo de memória desprezível.
    /// </summary>
    internal static class WeatherIcons
    {
        private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>();

        public static WeatherKind KindFromCode(int c) => c switch
        {
            0 => WeatherKind.Clear,
            1 or 2 => WeatherKind.PartlyCloudy,
            3 => WeatherKind.Cloudy,
            45 or 48 => WeatherKind.Fog,
            >= 51 and <= 57 => WeatherKind.Drizzle,
            (>= 61 and <= 67) or (>= 80 and <= 82) => WeatherKind.Rain,
            (>= 71 and <= 77) or 85 or 86 => WeatherKind.Snow,
            >= 95 => WeatherKind.Storm,
            _ => WeatherKind.Cloudy
        };

        public static ImageSource Get(WeatherKind kind, bool day)
        {
            string key = kind + (day ? "-d" : "-n");
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var g = new DrawingGroup();
            // Moldura fixa 100x100 para todos os ícones ficarem do mesmo tamanho
            g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 100, 100))));

            switch (kind)
            {
                case WeatherKind.Clear:
                    if (day) Sun(g, 50, 50, 30); else Moon(g, 50, 50, 28, true);
                    break;
                case WeatherKind.PartlyCloudy:
                    if (day) Sun(g, 62, 38, 26); else Moon(g, 64, 36, 22, false);
                    Cloud(g, 6, 46, 1.25, false);
                    break;
                case WeatherKind.Cloudy:
                    Cloud(g, 30, 24, 1.0, true);
                    Cloud(g, 8, 44, 1.25, false);
                    break;
                case WeatherKind.Fog:
                    Cloud(g, 18, 16, 1.0, false);
                    FogLines(g);
                    break;
                case WeatherKind.Drizzle:
                    if (day) Sun(g, 68, 28, 18);
                    Cloud(g, 12, 20, 1.15, false);
                    Drops(g, 3, 70, 8);
                    break;
                case WeatherKind.Rain:
                    Cloud(g, 12, 16, 1.2, true);
                    Drops(g, 4, 66, 18);
                    break;
                case WeatherKind.Snow:
                    Cloud(g, 12, 16, 1.2, false);
                    Flakes(g);
                    break;
                case WeatherKind.Storm:
                    Cloud(g, 12, 14, 1.2, true);
                    Bolt(g);
                    break;
            }

            g.Freeze();
            var img = new DrawingImage(g);
            img.Freeze();
            Cache[key] = img;
            return img;
        }

        private static void Sun(DrawingGroup g, double cx, double cy, double r)
        {
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0xFF, 0xC1, 0x07), 0.55));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xA0, 0x00), 1.0));
            g.Children.Add(new GeometryDrawing(glow, null, new EllipseGeometry(new Point(cx, cy), r * 1.45, r * 1.45)));

            var core = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.3),
                Center = new Point(0.4, 0.38),
                RadiusX = 0.75,
                RadiusY = 0.75
            };
            core.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xE0, 0x82), 0.0));
            core.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xB3, 0x00), 0.55));
            core.GradientStops.Add(new GradientStop(Color.FromRgb(0xFB, 0x8C, 0x00), 1.0));
            g.Children.Add(new GeometryDrawing(core, null, new EllipseGeometry(new Point(cx, cy), r, r)));
        }

        private static void Moon(DrawingGroup g, double cx, double cy, double r, bool stars)
        {
            var moon = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(new Point(cx, cy), r, r),
                new EllipseGeometry(new Point(cx + r * 0.5, cy - r * 0.35), r * 0.85, r * 0.85));
            var fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0xF4, 0xC7), Color.FromRgb(0xE9, 0xC9, 0x6A), 45);
            g.Children.Add(new GeometryDrawing(fill, null, moon));

            if (!stars) return;
            var star = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xFF));
            foreach (var (x, y, s) in new[] { (78.0, 26.0, 2.6), (86.0, 48.0, 1.8), (70.0, 76.0, 2.0) })
                g.Children.Add(new GeometryDrawing(star, null, new EllipseGeometry(new Point(x, y), s, s)));
        }

        /// <summary>Nuvem desenhada numa caixa local de 64x36, depois escalada e posicionada.</summary>
        private static void Cloud(DrawingGroup g, double x, double y, double scale, bool dark)
        {
            var geo = new GeometryGroup { FillRule = FillRule.Nonzero };
            geo.Children.Add(new RectangleGeometry(new Rect(0, 16, 64, 20), 10, 10));
            geo.Children.Add(new EllipseGeometry(new Point(18, 21), 12, 12));
            geo.Children.Add(new EllipseGeometry(new Point(36, 16), 16, 16));
            geo.Children.Add(new EllipseGeometry(new Point(51, 23), 11, 11));

            var t = new TransformGroup();
            t.Children.Add(new ScaleTransform(scale, scale));
            t.Children.Add(new TranslateTransform(x, y));
            geo.Transform = t;

            var brush = dark
                ? new LinearGradientBrush(Color.FromRgb(0xA4, 0xA9, 0xBA), Color.FromRgb(0x5E, 0x63, 0x77), 90)
                : new LinearGradientBrush(Color.FromRgb(0xFA, 0xFA, 0xFC), Color.FromRgb(0xA9, 0xAC, 0xB8), 90);
            g.Children.Add(new GeometryDrawing(brush, null, geo));
        }

        private static void Drops(DrawingGroup g, int count, double y, double length)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)), 4)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            var group = new GeometryGroup();
            double startX = 50 - (count - 1) * 7;
            for (int i = 0; i < count; i++)
            {
                double x = startX + i * 14;
                group.Children.Add(new LineGeometry(new Point(x, y), new Point(x - length * 0.3, y + length)));
            }
            g.Children.Add(new GeometryDrawing(null, pen, group));
        }

        private static void Flakes(DrawingGroup g)
        {
            var b = new SolidColorBrush(Color.FromRgb(0xF5, 0xF9, 0xFF));
            foreach (var (x, y) in new[] { (32.0, 72.0), (50.0, 78.0), (68.0, 72.0), (40.0, 90.0), (60.0, 90.0) })
                g.Children.Add(new GeometryDrawing(b, null, new EllipseGeometry(new Point(x, y), 3.8, 3.8)));
        }

        private static void Bolt(DrawingGroup g)
        {
            var bolt = Geometry.Parse("M 52,56 L 39,79 L 50,79 L 43,98 L 66,71 L 54,71 L 61,56 Z");
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)), null, bolt));
        }

        private static void FogLines(DrawingGroup g)
        {
            var b = new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD8));
            g.Children.Add(new GeometryDrawing(b, null, new RectangleGeometry(new Rect(14, 62, 72, 6), 3, 3)));
            g.Children.Add(new GeometryDrawing(b, null, new RectangleGeometry(new Rect(24, 74, 60, 6), 3, 3)));
            g.Children.Add(new GeometryDrawing(b, null, new RectangleGeometry(new Rect(14, 86, 64, 6), 3, 3)));
        }
    }
}
