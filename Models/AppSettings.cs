using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeskWidgets.Models
{
    public class AppSettings
    {
        // Posição / tamanho (-99999 = ainda não posicionado)
        public double Left { get; set; } = -99999;
        public double Top { get; set; } = -99999;
        public double Scale { get; set; } = 1.0;

        // Aparência
        public string PaletteName { get; set; } = "Índigo";
        public string GradientTop { get; set; } = "#3B35D6";
        public string GradientBottom { get; set; } = "#020208";
        public string Accent { get; set; } = "#FFFFFF";
        public string TextColor { get; set; } = "#FFFFFF";
        public string GradientDirection { get; set; } = "Vertical"; // Vertical | Horizontal | Diagonal
        public double TileOpacity { get; set; } = 1.0;               // 0..1 (só o fundo; o texto fica sólido)
        public double CornerRadius { get; set; } = 10;

        // Relógio
        public string ClockFont { get; set; } = "Times New Roman";
        public double ClockSize { get; set; } = 76;
        public bool ClockBold { get; set; } = true;
        public bool Clock24h { get; set; } = true;
        public bool ShowSeconds { get; set; } = false;
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

        // Comportamento
        public bool Locked { get; set; } = false;
        public string ZMode { get; set; } = "Desktop"; // Desktop | Normal | Topmost
        public bool StartWithWindows { get; set; } = false;

        // Widgets visíveis
        public bool ShowNotes { get; set; } = true;
        public bool ShowBattery { get; set; } = true;
        public bool ShowImage { get; set; } = true;
        public bool ShowWeather { get; set; } = true;
        public bool ShowClock { get; set; } = true;
        public bool ShowCalendar { get; set; } = true;

        // Bateria
        public List<string> HiddenDevices { get; set; } = new List<string>();
        public int BatteryRefreshMinutes { get; set; } = 3;
    }

    public class Palette
    {
        public Palette(string name, string top, string bottom, string accent, string text)
        {
            Name = name; Top = top; Bottom = bottom; Accent = accent; Text = text;
        }
        public string Name { get; }
        public string Top { get; }
        public string Bottom { get; }
        public string Accent { get; }
        public string Text { get; }
    }

    public static class Palettes
    {
        public static readonly Palette[] All =
        {
            new Palette("Índigo",     "#3B35D6", "#020208", "#FFFFFF", "#FFFFFF"),
            new Palette("Meia-noite", "#1E3A8A", "#020617", "#93C5FD", "#F8FAFC"),
            new Palette("Aurora",     "#0EA5A4", "#1E1B4B", "#A7F3D0", "#FFFFFF"),
            new Palette("Pôr do sol", "#F97316", "#3B0764", "#FDE68A", "#FFFFFF"),
            new Palette("Framboesa",  "#DB2777", "#1F0A1E", "#FBCFE8", "#FFFFFF"),
            new Palette("Floresta",   "#15803D", "#052E16", "#BBF7D0", "#F0FDF4"),
            new Palette("Vinho",      "#7F1D1D", "#0C0A09", "#FCA5A5", "#FFF1F2"),
            new Palette("Grafite",    "#3F3F46", "#09090B", "#E4E4E7", "#FAFAFA"),
            new Palette("Céu",        "#7DD3FC", "#E0F2FE", "#0369A1", "#0C1A2B"),
            new Palette("Papel",      "#F5F5F4", "#D6D3D1", "#1C1917", "#1C1917"),
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

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class DeviceBattery
    {
        public string Key { get; set; }    // identificador estável (usado para ocultar)
        public string Name { get; set; }
        public int Level { get; set; }     // 0..100
        public string Glyph { get; set; }  // ícone Segoe Fluent
        public string Source { get; set; } // bt | ble | sys | pad
    }
}
