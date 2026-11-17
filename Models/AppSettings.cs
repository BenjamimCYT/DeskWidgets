using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DeskWidgets.Models
{
    public class AppSettings
    {
        // Versão do layout: 0 = widget único (v1.x), 2 = um widget por janela
        public int LayoutVersion { get; set; } = 0;

        // Posição / tamanho do widget único antigo (usado só para migrar)
        public double Left { get; set; } = -99999;
        public double Top { get; set; } = -99999;
        public double Scale { get; set; } = 1.0;

        // Layout de cada widget (chave = id do widget)
        public Dictionary<string, WidgetLayout> Widgets { get; set; } = new Dictionary<string, WidgetLayout>();
        public bool SnapToGrid { get; set; } = true;
        public int GridSize { get; set; } = 10;
        public bool SnapToWidgets { get; set; } = true;
        public double WidgetGap { get; set; } = 10;

        // Tema global
        public string PaletteName { get; set; } = "Índigo";
        public string GradientTop { get; set; } = "#3B35D6";
        public string GradientBottom { get; set; } = "#020208";
        public string Accent { get; set; } = "#FFFFFF";
        public string TextColor { get; set; } = "#FFFFFF";
        public string GradientDirection { get; set; } = "Vertical"; // Vertical | Horizontal | Diagonal
        public double TileOpacity { get; set; } = 1.0;               // 0..1 (só o fundo; o texto fica sólido)
        public double CornerRadius { get; set; } = 14;
        public double BorderOpacity { get; set; } = 0.10;            // contorno sutil dos blocos (0 = sem contorno)
        public string UiFont { get; set; } = "Segoe UI Variable Text";
        public List<ThemeDef> CustomThemes { get; set; } = new List<ThemeDef>();
        public bool AnimatedIcons { get; set; } = true;

        // Relógio
        public string ClockFont { get; set; } = "Times New Roman";
        public double ClockSize { get; set; } = 76;
        public bool ClockBold { get; set; } = true;
        public bool Clock24h { get; set; } = true;
        public bool ShowSeconds { get; set; } = false;
        public bool ClockShowDate { get; set; } = true;
        public string DateCulture { get; set; } = "pt-BR";

        // Clima (Open-Meteo, sem chave de API)
        public string City { get; set; } = "São Paulo";
        public double Latitude { get; set; } = -23.5475;
        public double Longitude { get; set; } = -46.63611;
        public string TemperatureUnit { get; set; } = "celsius"; // celsius | fahrenheit
        public bool WeatherTransparentTile { get; set; } = true;

        // Imagem
        public string ImagePath { get; set; } = "";
        public bool ImagePixelArt { get; set; } = true;

        // Música
        public bool MusicShowArt { get; set; } = true;
        public bool MusicShowProgress { get; set; } = true;
        public bool MusicArtBackground { get; set; } = false;

        // Pomodoro
        public int PomodoroFocus { get; set; } = 25;
        public int PomodoroShort { get; set; } = 5;
        public int PomodoroLong { get; set; } = 15;
        public int PomodoroCycles { get; set; } = 4;
        public bool PomodoroAutoStart { get; set; } = false;
        public bool PomodoroSound { get; set; } = true;
        public bool PomodoroNotify { get; set; } = true;

        // Componentes
        public int HardwareRefreshSeconds { get; set; } = 5;
        public int HardwareWarnTemp { get; set; } = 80;
        public bool HwShowCpuTemp { get; set; } = true;
        public bool HwShowGpuTemp { get; set; } = true;
        public bool HwShowDiskTemp { get; set; } = true;
        public bool HwShowCpuLoad { get; set; } = true;
        public bool HwShowRam { get; set; } = true;

        // Comportamento
        public bool Locked { get; set; } = false;
        public string ZMode { get; set; } = "Desktop"; // Desktop | Normal | Topmost
        public bool StartWithWindows { get; set; } = false;
        public bool StartMenuShortcut { get; set; } = true;

        // Widgets visíveis (v1.x, usados só para migrar)
        public bool ShowNotes { get; set; } = true;
        public bool ShowBattery { get; set; } = true;
        public bool ShowImage { get; set; } = true;
        public bool ShowWeather { get; set; } = true;
        public bool ShowClock { get; set; } = true;
        public bool ShowCalendar { get; set; } = true;

        // Notas: "local" ou "todoist"
        public string NotesSource { get; set; } = "local";
        public string TodoistToken { get; set; } = "";       // criptografado (DPAPI)
        public string TodoistProjectId { get; set; } = "";
        public string TodoistProjectName { get; set; } = "";
        public List<string> TodoistPendingDeletes { get; set; } = new List<string>();

        // Bateria
        public List<string> HiddenDevices { get; set; } = new List<string>();
        public int BatteryRefreshMinutes { get; set; } = 3;
        // Última porcentagem recebida de mouses 2.4 GHz (mostrada até chegar uma leitura nova)
        public Dictionary<string, int> LastVendorBattery { get; set; } = new Dictionary<string, int>();

        /// <summary>Tema global montado a partir dos campos acima.</summary>
        public ThemeDef GlobalTheme() => new ThemeDef
        {
            Name = PaletteName,
            Top = GradientTop,
            Bottom = GradientBottom,
            Accent = Accent,
            Text = TextColor
        };

        public WidgetLayout Layout(string id)
        {
            if (!Widgets.TryGetValue(id, out var l) || l == null)
            {
                l = new WidgetLayout();
                Widgets[id] = l;
            }
            return l;
        }
    }

    /// <summary>Posição, tamanho e tema de um widget (cada widget é uma janela própria).</summary>
    public class WidgetLayout
    {
        public bool Visible { get; set; } = true;
        public double Left { get; set; } = -99999;   // -99999 = ainda não posicionado
        public double Top { get; set; } = -99999;
        public double Width { get; set; } = 0;        // 0 = tamanho padrão do widget
        public double Height { get; set; } = 0;
        public string Theme { get; set; } = "";        // "" = segue o tema global
        public bool HasPosition => Left > -99990 && Top > -99990;
    }

    /// <summary>Esquema de cores de um tema (pronto ou criado por você).</summary>
    public class ThemeDef
    {
        public string Name { get; set; } = "";
        public string Top { get; set; } = "#3B35D6";
        public string Bottom { get; set; } = "#020208";
        public string Accent { get; set; } = "#FFFFFF";
        public string Text { get; set; } = "#FFFFFF";

        public ThemeDef() { }
        public ThemeDef(string name, string top, string bottom, string accent, string text)
        {
            Name = name; Top = top; Bottom = bottom; Accent = accent; Text = text;
        }
    }

    public static class Palettes
    {
        public static readonly ThemeDef[] All =
        {
            new ThemeDef("Índigo",     "#3B35D6", "#020208", "#FFFFFF", "#FFFFFF"),
            new ThemeDef("Meia-noite", "#1E3A8A", "#020617", "#93C5FD", "#F8FAFC"),
            new ThemeDef("Aurora",     "#0EA5A4", "#1E1B4B", "#A7F3D0", "#FFFFFF"),
            new ThemeDef("Pôr do sol", "#F97316", "#3B0764", "#FDE68A", "#FFFFFF"),
            new ThemeDef("Framboesa",  "#DB2777", "#1F0A1E", "#FBCFE8", "#FFFFFF"),
            new ThemeDef("Floresta",   "#15803D", "#052E16", "#BBF7D0", "#F0FDF4"),
            new ThemeDef("Vinho",      "#7F1D1D", "#0C0A09", "#FCA5A5", "#FFF1F2"),
            new ThemeDef("Grafite",    "#3F3F46", "#09090B", "#E4E4E7", "#FAFAFA"),
            new ThemeDef("Neon",       "#302B63", "#0F0C29", "#00F5D4", "#FFFFFF"),
            new ThemeDef("Lavanda",    "#A78BFA", "#4C1D95", "#FDF4FF", "#FFFFFF"),
            new ThemeDef("Oceano",     "#0284C7", "#082F49", "#67E8F9", "#F0F9FF"),
            new ThemeDef("Café",       "#78350F", "#1C1917", "#FCD34D", "#FEF3C7"),
            new ThemeDef("Vidro",      "#40FFFFFF", "#12FFFFFF", "#FFFFFF", "#FFFFFF"),
            new ThemeDef("Céu",        "#7DD3FC", "#E0F2FE", "#0369A1", "#0C1A2B"),
            new ThemeDef("Menta",      "#D1FAE5", "#A7F3D0", "#047857", "#064E3B"),
            new ThemeDef("Papel",      "#F5F5F4", "#D6D3D1", "#1C1917", "#1C1917"),
        };
    }

    public class NoteItem : INotifyPropertyChanged
    {
        private string _text = "";
        private bool _done;

        public string Text
        {
            get => _text;
            set { if (_text != value) { _text = value ?? ""; OnChanged(); } }
        }

        public bool Done
        {
            get => _done;
            set { if (_done != value) { _done = value; OnChanged(); } }
        }

        // Sincronização com o Todoist (vazios no modo local)
        public string RemoteId { get; set; }
        public string SyncedText { get; set; }
        public bool SyncedDone { get; set; }
        [JsonIgnore] public string TempId { get; set; }
        [JsonIgnore] public bool SyncFailed { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class DeviceBattery
    {
        public string Key { get; set; }    // identificador estável (usado para ocultar)
        public string Name { get; set; }
        public int Level { get; set; }     // 0..100 (-1 = aguardando o dispositivo informar)
        public string Glyph { get; set; }  // ícone Segoe Fluent
        public string Source { get; set; } // bt | ble | sys | pad
    }
}
