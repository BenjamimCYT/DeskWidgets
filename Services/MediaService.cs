using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace DeskWidgets.Services
{
    public class MediaState
    {
        public bool HasSession { get; set; }
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string AppId { get; set; } = "";
        public bool IsPlaying { get; set; }
        public bool CanNext { get; set; }
        public bool CanPrevious { get; set; }
        public bool CanPlayPause { get; set; }
        public ImageSource Art { get; set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTimeOffset PositionUpdated { get; set; }
    }

    /// <summary>
    /// "Tocando agora" do Windows (o mesmo do painel de volume): funciona com Spotify, YouTube no navegador,
    /// Apple Music, Deezer, players de vídeo etc. Só por eventos, sem polling.
    /// </summary>
    public static class MediaService
    {
        private static GlobalSystemMediaTransportControlsSessionManager _mgr;
        private static GlobalSystemMediaTransportControlsSession _session;
        private static string _artKey;
        private static ImageSource _art;
        private static bool _starting;

        public static MediaState Current { get; private set; } = new MediaState();
        /// <summary>Disparado numa thread qualquer; quem ouve deve voltar para a UI.</summary>
        public static event Action Changed;

        public static async Task StartAsync()
        {
            if (_mgr != null || _starting) return;
            _starting = true;
            try
            {
                _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mgr.CurrentSessionChanged += (s, e) => _ = AttachAsync();
                _mgr.SessionsChanged += (s, e) => _ = AttachAsync();
                await AttachAsync();
            }
            catch (Exception ex) { SettingsService.Log(ex); }
            finally { _starting = false; }
        }

        private static async Task AttachAsync()
        {
            try
            {
                var s = _mgr?.GetCurrentSession();
                if (!ReferenceEquals(s, _session))
                {
                    if (_session != null)
                    {
                        _session.MediaPropertiesChanged -= OnProps;
                        _session.PlaybackInfoChanged -= OnPlayback;
                        _session.TimelinePropertiesChanged -= OnTimeline;
                    }
                    _session = s;
                    if (_session != null)
                    {
                        _session.MediaPropertiesChanged += OnProps;
                        _session.PlaybackInfoChanged += OnPlayback;
                        _session.TimelinePropertiesChanged += OnTimeline;
                    }
                }
                await UpdateAsync(true);
            }
            catch (Exception ex) { SettingsService.Log(ex); }
        }

        private static void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = UpdateAsync(true);
        private static void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ = UpdateAsync(false);
        private static void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => _ = UpdateAsync(false);

        private static async Task UpdateAsync(bool props)
        {
            var s = _session;
            var st = new MediaState();
            try
            {
                if (s != null)
                {
                    st.HasSession = true;
                    st.AppId = s.SourceAppUserModelId ?? "";

                    var info = s.GetPlaybackInfo();
                    if (info != null)
                    {
                        st.IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                        st.CanNext = info.Controls?.IsNextEnabled == true;
                        st.CanPrevious = info.Controls?.IsPreviousEnabled == true;
                        st.CanPlayPause = info.Controls?.IsPlayPauseToggleEnabled == true
                                          || info.Controls?.IsPlayEnabled == true || info.Controls?.IsPauseEnabled == true;
                    }

                    var tl = s.GetTimelineProperties();
                    if (tl != null)
                    {
                        st.Position = tl.Position;
                        st.Duration = tl.EndTime - tl.StartTime;
                        st.PositionUpdated = tl.LastUpdatedTime;
                    }

                    if (props || Current.Title.Length == 0)
                    {
                        var mp = await s.TryGetMediaPropertiesAsync();
                        if (mp != null)
                        {
                            st.Title = mp.Title ?? "";
                            st.Artist = string.IsNullOrWhiteSpace(mp.Artist) ? mp.AlbumArtist ?? "" : mp.Artist;
                            st.Album = mp.AlbumTitle ?? "";
                            string key = st.Title + "|" + st.Artist + "|" + st.Album;
                            if (key != _artKey)
                            {
                                _artKey = key;
                                _art = mp.Thumbnail != null ? await LoadArtAsync(mp.Thumbnail) : null;
                            }
                        }
                    }
                    else
                    {
                        st.Title = Current.Title;
                        st.Artist = Current.Artist;
                        st.Album = Current.Album;
                    }
                    st.Art = _art;
                }
            }
            catch (Exception ex) { SettingsService.Log(ex); }

            Current = st;
            Changed?.Invoke();
        }

        private static async Task<ImageSource> LoadArtAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
        {
            try
            {
                using var ras = await reference.OpenReadAsync();
                using var src = ras.AsStreamForRead();
                var ms = new MemoryStream();
                await src.CopyToAsync(ms);
                ms.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 200;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                SettingsService.Log(ex);
                return null;
            }
        }

        public static async void PlayPause()
        {
            try { if (_session != null) await _session.TryTogglePlayPauseAsync(); }
            catch (Exception ex) { SettingsService.Log(ex); }
        }

        public static async void Next()
        {
            try { if (_session != null) await _session.TrySkipNextAsync(); }
            catch (Exception ex) { SettingsService.Log(ex); }
        }

        public static async void Previous()
        {
            try { if (_session != null) await _session.TrySkipPreviousAsync(); }
            catch (Exception ex) { SettingsService.Log(ex); }
        }
    }
}
