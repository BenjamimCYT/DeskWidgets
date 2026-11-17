using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using DeskWidgets.Models;
using DeskWidgets.Services;

namespace DeskWidgets.Helpers
{
    /// <summary>Resolve o tema de cada widget (global, pronto ou personalizado) e gera os pincéis.</summary>
    internal static class ThemeService
    {
        private static AppSettings S => SettingsService.Current;

        public static IEnumerable<ThemeDef> AllThemes => Palettes.All.Concat(S.CustomThemes ?? new List<ThemeDef>());

        public static ThemeDef Find(string name)
            => string.IsNullOrWhiteSpace(name) ? null
             : AllThemes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        public static ThemeDef ForWidget(string id)
        {
            string name = S.Layout(id).Theme;
            return Find(name) ?? S.GlobalTheme();
        }

        public static bool IsCustom(string name) =>
            S.CustomThemes?.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) == true;

        /// <summary>Coloca os pincéis do tema no dicionário de recursos (usados via DynamicResource).</summary>
        public static void ApplyBrushes(ResourceDictionary r, ThemeDef t)
        {
            var text = ColorUtil.Parse(t.Text, Colors.White);
            var accent = ColorUtil.Parse(t.Accent, Colors.White);
            r["TextBrush"] = ColorUtil.Solid(text);
            r["SubtleBrush"] = ColorUtil.Solid(text, 0.14);
            r["FaintBrush"] = ColorUtil.Solid(text, 0.28);
            r["MutedBrush"] = ColorUtil.Solid(text, 0.65);
            r["AccentBrush"] = ColorUtil.Solid(accent);
            r["AccentSoftBrush"] = ColorUtil.Solid(accent, 0.22);
            r["AccentTextBrush"] = ColorUtil.ContrastFor(accent);
            r["HoverBrush"] = ColorUtil.Solid(text, 0.16);
            r["PressedBrush"] = ColorUtil.Solid(text, 0.26);
        }
    }
}
