using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeskWidgets.Services;

namespace DeskWidgets.Widgets
{
    public partial class MusicWidget : WidgetBase
    {
        public override string Id => "music";
        public override string DisplayName => "Música";
        public override string Glyph => "";
        public override Size BaseSize => new Size(320, 112);
        public override Size MinSize => new Size(200, 80);

        // Só anda 1x por segundo enquanto toca (para a barra de progresso)
        private readonly DispatcherTimer _progress = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        private bool _started;

        public MusicWidget()
        {
            InitializeComponent();
            _progress.Tick += (s, e) => UpdateProgress();
            MediaService.Changed += () => Dispatcher.BeginInvoke(new Action(Render));
            ProgressRow.SizeChanged += (s, e) => UpdateProgress();
        }

        public override void ApplySettings() => Render();

        public override async void OnShown()
        {
            if (!_started)
            {
                _started = true;
                await MediaService.StartAsync();
            }
            Render();
        }

        public override void OnHidden()
        {
            _progress.Stop();
            Eq.IsActive = false;
        }

        public override IEnumerable<MenuItem> MenuItems()
        {
            yield return Item("Tocar / pausar", (s, e) => MediaService.PlayPause());
            yield return Item("Próxima", (s, e) => MediaService.Next());
            yield return Item("Anterior", (s, e) => MediaService.Previous());
        }

        private void Render()
        {
            var m = MediaService.Current;
            bool has = m.HasSession && (m.Title.Length > 0 || m.Artist.Length > 0);

            TitleText.Text = has ? (m.Title.Length > 0 ? m.Title : "Sem título") : "Nada tocando";
            ArtistText.Text = has ? Join(m.Artist, m.Album) : "Abra o Spotify, o YouTube ou outro player";
            ToolTip = has ? $"{m.Title}\n{m.Artist}\n{m.Album}".Trim() : null;

            PlayButton.Content = m.IsPlaying ? "" : "";
            PlayButton.IsEnabled = m.HasSession;
            PrevButton.IsEnabled = m.HasSession && m.CanPrevious;
            NextButton.IsEnabled = m.HasSession && m.CanNext;

            bool showArt = S.MusicShowArt;
            ArtBox.Visibility = showArt ? Visibility.Visible : Visibility.Collapsed;
            if (m.Art != null)
            {
                var b = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(b, BitmapScalingMode.HighQuality);
                b.Freeze();
                ArtImage.Background = b;
                ArtPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                ArtImage.Background = null;
                ArtPlaceholder.Visibility = Visibility.Visible;
            }
            ArtBackdrop.Source = m.Art;
            ArtBackdrop.Visibility = S.MusicArtBackground && m.Art != null ? Visibility.Visible : Visibility.Collapsed;

            Eq.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            Eq.IsActive = m.IsPlaying && HostVisible;

            bool progress = S.MusicShowProgress && has && m.Duration > TimeSpan.FromSeconds(1);
            ProgressRow.Visibility = progress ? Visibility.Visible : Visibility.Hidden;
            Elapsed.Visibility = Total.Visibility = progress ? Visibility.Visible : Visibility.Collapsed;
            if (progress && m.IsPlaying && HostVisible) _progress.Start(); else _progress.Stop();
            UpdateProgress();
        }

        private void UpdateProgress()
        {
            var m = MediaService.Current;
            if (m.Duration <= TimeSpan.Zero) { ProgressFill.Width = 0; return; }

            var pos = m.Position;
            if (m.IsPlaying && m.PositionUpdated != default)
                pos += DateTimeOffset.Now - m.PositionUpdated;
            if (pos > m.Duration) pos = m.Duration;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;

            double f = pos.TotalSeconds / m.Duration.TotalSeconds;
            ProgressFill.Width = Math.Max(0, ProgressRow.ActualWidth * f);
            Elapsed.Text = Fmt(pos);
            Total.Text = Fmt(m.Duration);
        }

        private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

        private static string Join(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a)) return b ?? "";
            if (string.IsNullOrWhiteSpace(b)) return a;
            return a + " · " + b;
        }

        private void Play_Click(object sender, RoutedEventArgs e) => MediaService.PlayPause();
        private void Next_Click(object sender, RoutedEventArgs e) => MediaService.Next();
        private void Prev_Click(object sender, RoutedEventArgs e) => MediaService.Previous();
    }
}
