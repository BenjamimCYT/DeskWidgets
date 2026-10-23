using System;
using System.Windows;
using System.Windows.Media;
using DeskWidgets.Models;

namespace DeskWidgets.Helpers
{
    internal static class ColorUtil
    {
        public static bool TryParse(string hex, out Color color)
        {
            color = Colors.White;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            string s = hex.Trim();
            if (!s.StartsWith("#") && (s.Length == 6 || s.Length == 8)) s = "#" + s;
            try
            {
                if (ColorConverter.ConvertFromString(s) is Color c) { color = c; return true; }
            }
            catch { }
            return false;
        }

        public static Color Parse(string hex, Color fallback) => TryParse(hex, out var c) ? c : fallback;

        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public static SolidColorBrush Solid(Color c, double opacity = 1)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(c.A * Math.Clamp(opacity, 0, 1)), c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        /// <summary>Preto ou branco, o que tiver mais contraste com a cor.</summary>
        public static SolidColorBrush ContrastFor(Color c)
        {
            double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            return Solid(lum > 150 ? Color.FromRgb(0x14, 0x14, 0x24) : Colors.White);
        }

        /// <summary>
        /// Gradiente do fundo dos blocos. O alfa mínimo é 1 (invisível, mas ainda clicável/arrastável):
        /// com alfa 0 o Windows deixaria o clique "atravessar" a janela.
        /// </summary>
        public static Brush Gradient(AppSettings s, double? opacityOverride = null)
        {
            double op = Math.Clamp(opacityOverride ?? s.TileOpacity, 0, 1);
            var top = Parse(s.GradientTop, Color.FromRgb(0x3B, 0x35, 0xD6));
            var bottom = Parse(s.GradientBottom, Colors.Black);
            top.A = (byte)Math.Max(1, Math.Round(top.A * op));
            bottom.A = (byte)Math.Max(1, Math.Round(bottom.A * op));

            Point start, end;
            switch (s.GradientDirection)
            {
                case "Horizontal": start = new Point(0, 0.5); end = new Point(1, 0.5); break;
                case "Diagonal": start = new Point(0, 0); end = new Point(1, 1); break;
                default: start = new Point(0.5, 0); end = new Point(0.5, 1); break;
            }

            var brush = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(top, 0),
                new GradientStop(bottom, 1)
            }, start, end);
            brush.Freeze();
            return brush;
        }

        public static Brush AlmostTransparent()
        {
            var b = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            b.Freeze();
            return b;
        }
    }
}
