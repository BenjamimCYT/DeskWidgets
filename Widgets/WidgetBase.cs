using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskWidgets.Models;
using DeskWidgets.Services;

namespace DeskWidgets.Widgets
{
    /// <summary>Conteúdo de um widget. Cada um vive na sua própria janela (<see cref="WidgetHost"/>).</summary>
    public abstract class WidgetBase : UserControl
    {
        protected static AppSettings S => SettingsService.Current;

        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        /// <summary>Ícone Segoe Fluent usado nas configurações.</summary>
        public abstract string Glyph { get; }
        /// <summary>Tamanho de referência: o conteúdo é desenhado nele e ampliado pelo lado menor.</summary>
        public abstract Size BaseSize { get; }
        public virtual Size DefaultSize => BaseSize;
        public virtual Size MinSize => new Size(System.Math.Round(BaseSize.Width * 0.55), System.Math.Round(BaseSize.Height * 0.55));

        public WidgetHost Host { get; internal set; }

        /// <summary>A janela do widget está na tela.</summary>
        protected bool HostVisible => Host?.IsVisible == true;

        /// <summary>true se o último clique foi, na verdade, o fim de um arraste.</summary>
        protected bool WasDragged => Host?.Dragged == true;

        /// <summary>Fundo do bloco (o widget de imagem troca pela foto; o clima pode ficar sem fundo).</summary>
        public virtual Brush TileBackground(Brush themeBrush) => themeBrush;
        public virtual bool ShowTileBorder => true;

        /// <summary>Configurações mudaram (cores, fontes, opções do widget).</summary>
        public virtual void ApplySettings() { }

        /// <summary>A janela apareceu: hora de ligar timers e buscar dados.</summary>
        public virtual void OnShown() { }

        /// <summary>A janela foi ocultada: desligue timers para não gastar nada.</summary>
        public virtual void OnHidden() { }

        public virtual void Refresh() { }

        /// <summary>Itens extras no menu do botão direito.</summary>
        public virtual IEnumerable<MenuItem> MenuItems() { yield break; }

        protected static MenuItem Item(string header, RoutedEventHandler click)
        {
            var m = new MenuItem { Header = header };
            m.Click += click;
            return m;
        }
    }
}
