using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using DeskWidgets.Models;

namespace DeskWidgets.Services
{
    public static class SettingsService
    {
        public static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskWidgets");

        private static string SettingsFile => Path.Combine(Folder, "settings.json");
        private static string NotesFile => Path.Combine(Folder, "notas.json");
        private static string LogFile => Path.Combine(Folder, "erros.log");

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly object LogLock = new object();
        private static DispatcherTimer _saveTimer;

        public static AppSettings Current { get; private set; } = new AppSettings();
        public static ObservableCollection<NoteItem> Notes { get; } = new ObservableCollection<NoteItem>();
        public static event Action NotesChanged;

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                if (File.Exists(SettingsFile))
                    Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), Json) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Log(ex);
                Current = new AppSettings();
            }
            Current.HiddenDevices ??= new List<string>();
            Current.LastVendorBattery ??= new Dictionary<string, int>();
            Current.TodoistPendingDeletes ??= new List<string>();
            Current.Widgets ??= new Dictionary<string, Models.WidgetLayout>();
            Current.CustomThemes ??= new List<ThemeDef>();

            try
            {
                if (File.Exists(NotesFile))
                {
                    var items = JsonSerializer.Deserialize<List<NoteItem>>(File.ReadAllText(NotesFile), Json);
                    if (items != null)
                        foreach (var n in items) Notes.Add(n);
                }
            }
            catch (Exception ex) { Log(ex); }

            foreach (var n in Notes) n.PropertyChanged += OnNoteChanged;
            Notes.CollectionChanged += OnNotesCollectionChanged;
        }

        private static void OnNotesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null) foreach (NoteItem n in e.NewItems) n.PropertyChanged += OnNoteChanged;
            if (e.OldItems != null) foreach (NoteItem n in e.OldItems) n.PropertyChanged -= OnNoteChanged;
            NotesChanged?.Invoke();
            SaveSoon();
        }

        private static void OnNoteChanged(object sender, PropertyChangedEventArgs e)
        {
            NotesChanged?.Invoke();
            SaveSoon();
        }

        /// <summary>Agrupa várias mudanças seguidas numa única gravação em disco.</summary>
        public static void SaveSoon()
        {
            if (_saveTimer == null)
            {
                _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(800) };
                _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); SaveNow(); };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public static void SaveNow()
        {
            _saveTimer?.Stop();
            try
            {
                Directory.CreateDirectory(Folder);
                WriteAtomic(SettingsFile, JsonSerializer.Serialize(Current, Json));
                WriteAtomic(NotesFile, JsonSerializer.Serialize(Notes.ToList(), Json));
            }
            catch (Exception ex) { Log(ex); }
        }

        private static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, true);
        }

        /// <summary>Volta cores, fontes e estilo ao padrão. Layout, notas, cidade, imagem e temas salvos continuam.</summary>
        public static void ResetAppearance()
        {
            var d = new AppSettings();
            var c = Current;
            c.PaletteName = d.PaletteName;
            c.GradientTop = d.GradientTop;
            c.GradientBottom = d.GradientBottom;
            c.Accent = d.Accent;
            c.TextColor = d.TextColor;
            c.GradientDirection = d.GradientDirection;
            c.TileOpacity = d.TileOpacity;
            c.CornerRadius = d.CornerRadius;
            c.BorderOpacity = d.BorderOpacity;
            c.UiFont = d.UiFont;
            c.AnimatedIcons = d.AnimatedIcons;
            c.ClockFont = d.ClockFont;
            c.ClockBold = d.ClockBold;
            c.Clock24h = d.Clock24h;
            c.ShowSeconds = d.ShowSeconds;
            c.ClockShowDate = d.ClockShowDate;
            c.DateCulture = d.DateCulture;
            c.WeatherTransparentTile = d.WeatherTransparentTile;
            foreach (var l in c.Widgets.Values) l.Theme = "";
            SaveSoon();
        }

        /// <summary>Guarda uma cópia das notas locais antes de trocar para o Todoist.</summary>
        public static void BackupNotes()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, $"notas-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.WriteAllText(path, JsonSerializer.Serialize(Notes.ToList(), Json));
            }
            catch (Exception ex) { Log(ex); }
        }

        public static void Log(Exception ex)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(Folder);
                    var fi = new FileInfo(LogFile);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete(); // não deixa o log crescer sem limite
                    File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
                }
            }
            catch { /* log nunca pode derrubar o app */ }
        }
    }
}
