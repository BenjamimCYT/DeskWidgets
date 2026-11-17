using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using DeskWidgets.Helpers;
using DeskWidgets.Services;

namespace DeskWidgets.Widgets
{
    /// <summary>Uma foto ou arte sua (com modo pixel art para ampliar sem borrar).</summary>
    public class ImageWidget : WidgetBase
    {
        public override string Id => "image";
        public override string DisplayName => "Imagem";
        public override string Glyph => "";
        public override Size BaseSize => new Size(110, 100);
        public override Size DefaultSize => new Size(120, 100);
        public override Size MinSize => new Size(50, 50);

        private readonly StackPanel _placeholder;
        private ImageBrush _brush;

        public ImageWidget()
        {
            _placeholder = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            _placeholder.Children.Add(new TextBlock
            {
                Text = "", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.7,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets")
            });
            _placeholder.Children.Add(new TextBlock { Text = "Escolher imagem", FontSize = 11, Margin = new Thickness(0, 6, 0, 0), Opacity = 0.75 });
            Content = new Grid { Background = Brushes.Transparent, Children = { _placeholder } };
            MouseLeftButtonUp += (s, e) =>
            {
                if (WasDragged) return;
                if (_brush == null) PickImage();
            };
        }

        public override Brush TileBackground(Brush themeBrush) => (Brush)_brush ?? themeBrush;

        public override void ApplySettings() => LoadImage();

        public override IEnumerable<MenuItem> MenuItems()
        {
            yield return Item("Escolher imagem…", (s, e) => PickImage());
            var remove = Item("Remover imagem", (s, e) => ClearImage());
            remove.IsEnabled = !string.IsNullOrWhiteSpace(S.ImagePath);
            yield return remove;
        }

        private void LoadImage()
        {
            _brush = null;
            if (!string.IsNullOrWhiteSpace(S.ImagePath) && System.IO.File.Exists(S.ImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad; // não trava o arquivo
                    bmp.UriSource = new Uri(S.ImagePath);
                    // Decodifica pequeno: uma foto de 4000px não ocupa 60 MB de RAM à toa
                    if (!S.ImagePixelArt) bmp.DecodePixelWidth = 600;
                    bmp.EndInit();
                    bmp.Freeze();

                    var mode = S.ImagePixelArt ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;
                    var brush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                    RenderOptions.SetBitmapScalingMode(brush, mode);
                    brush.Freeze();
                    _brush = brush;
                    if (Host != null) RenderOptions.SetBitmapScalingMode(Host, mode);
                }
                catch (Exception ex) { SettingsService.Log(ex); }
            }
            _placeholder.Visibility = _brush == null ? Visibility.Visible : Visibility.Collapsed;
            Cursor = _brush == null ? Cursors.Hand : null;
            Host?.RefreshBackground();
        }

        public void PickImage()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Escolher imagem do widget",
                Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|Todos os arquivos|*.*"
            };
            Host?.BringToFront();
            if (dlg.ShowDialog(Host) == true)
            {
                S.ImagePath = dlg.FileName;
                SettingsService.SaveSoon();
                if (!S.Layout(Id).Visible) WidgetManager.SetVisible(Id, true);
                LoadImage();
                ResourceSaver.Trim();
            }
            Host?.ApplyZOrder();
        }

        public void ClearImage()
        {
            S.ImagePath = "";
            SettingsService.SaveSoon();
            LoadImage();
        }
    }
}
