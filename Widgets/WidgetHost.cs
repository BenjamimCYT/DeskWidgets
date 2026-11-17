using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using DeskWidgets.Controls;
using DeskWidgets.Helpers;
using DeskWidgets.Models;
using DeskWidgets.Services;

namespace DeskWidgets.Widgets
{
    /// <summary>
    /// Janela transparente que hospeda um único widget: arrastar para mover (com encaixe magnético),
    /// alça no canto para redimensionar, Ctrl + roda para ampliar, menu de contexto e camada (fundo/normal/topo).
    /// </summary>
    public class WidgetHost : Window
    {
        private static AppSettings S => SettingsService.Current;

        public WidgetBase Widget { get; }
        public string Id => Widget.Id;
        public bool Dragged { get; private set; }

        private readonly Grid _root = new Grid();
        private readonly Border _tile = new Border();
        private readonly ScaleBox _box = new ScaleBox();
        private readonly Thumb _grip;
        private readonly Rectangle _outline;
        private readonly Border _label;

        private IntPtr _hwnd;
        private bool _allowFront;

        // arraste manual (para poder encaixar)
        private bool _maybeDrag, _dragging;
        private Point _downCursor;
        private double _startLeft, _startTop;

        // redimensionamento
        private Point _resizeCursor;
        private double _startW, _startH;

        public WidgetHost(WidgetBase widget)
        {
            Widget = widget;
            widget.Host = this;

            Title = "DeskWidgets · " + widget.DisplayName;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Manual;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            ShowActivated = false;

            _box.Child = widget;
            widget.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            _tile.Child = _box;
            _tile.BorderThickness = new Thickness(1);
            _root.Children.Add(_tile);
            _tile.SizeChanged += (s, e) => UpdateClip();

            _outline = new Rectangle
            {
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            _outline.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
            _root.Children.Add(_outline);

            var labelText = new TextBlock { Text = widget.DisplayName, FontSize = 11, FontWeight = FontWeights.SemiBold };
            labelText.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
            _label = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2, 7, 3),
                Margin = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                Child = labelText
            };
            _label.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            _root.Children.Add(_label);

            _grip = new Thumb
            {
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNWSE,
                Opacity = 0,
                ToolTip = "Arraste para redimensionar",
                Template = GripTemplate()
            };
            _grip.DragStarted += Grip_DragStarted;
            _grip.DragDelta += Grip_DragDelta;
            _grip.DragCompleted += (s, e) => SaveLayout();
            _root.Children.Add(_grip);

            Content = _root;

            MouseEnter += (s, e) => UpdateChrome();
            MouseLeave += (s, e) => UpdateChrome();
            SourceInitialized += OnSourceInitialized;
            Deactivated += (s, e) =>
            {
                if (_allowFront) { _allowFront = false; ApplyZOrder(); }
            };
            IsKeyboardFocusWithinChanged += (s, e) =>
            {
                if (IsKeyboardFocusWithin) _allowFront = true;
            };
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) Widget.OnShown(); else Widget.OnHidden();
            };
            ContextMenu = new ContextMenu();
            ContextMenu.Opened += (s, e) => BuildMenu();
        }

        private static ControlTemplate GripTemplate()
        {
            // Três riscos diagonais discretos no canto
            var t = new ControlTemplate(typeof(Thumb));
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            var path = new FrameworkElementFactory(typeof(Path));
            path.SetValue(Path.DataProperty, Geometry.Parse("M15,6 L6,15 M15,10 L10,15 M15,14 L14,15"));
            path.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            path.SetValue(Shape.StrokeThicknessProperty, 1.4);
            path.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            path.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            path.SetValue(UIElement.OpacityProperty, 0.8);
            grid.AppendChild(path);
            t.VisualTree = grid;
            return t;
        }

        // ================== Janela / camada ==================

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            ex = (ex | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW;
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
            ApplyZOrder();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_WINDOWPOSCHANGING && S.ZMode == "Desktop" && !_allowFront && !WidgetManager.EditMode)
            {
                var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
                pos.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
                pos.flags &= ~NativeMethods.SWP_NOZORDER;
                Marshal.StructureToPtr(pos, lParam, false);
            }
            return IntPtr.Zero;
        }

        public void ApplyZOrder()
        {
            Topmost = S.ZMode == "Topmost" || WidgetManager.EditMode;
            if (S.ZMode == "Desktop" && _hwnd != IntPtr.Zero && !_allowFront && !WidgetManager.EditMode)
            {
                NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        }

        /// <summary>Traz para frente até você clicar em outra janela (usado após Win+D e ao digitar).</summary>
        public void BringToFront(bool activate = true)
        {
            if (!IsVisible) return;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            _allowFront = true;
            if (activate) Activate();
            else NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        // ================== Aparência ==================

        public void ApplySettings()
        {
            var theme = ThemeService.ForWidget(Id);
            ThemeService.ApplyBrushes(Resources, theme);
            var text = ColorUtil.Parse(theme.Text, Colors.White);
            TextElement.SetForeground(_root, ColorUtil.Solid(text));
            try { FontFamily = new FontFamily((string.IsNullOrWhiteSpace(S.UiFont) ? "Segoe UI Variable Text" : S.UiFont) + ", Segoe UI"); } catch { }

            double radius = Math.Clamp(S.CornerRadius, 0, 40);
            _tile.CornerRadius = new CornerRadius(radius);
            _outline.RadiusX = _outline.RadiusY = radius;
            _tile.Background = Widget.TileBackground(ColorUtil.Gradient(theme, S.TileOpacity, S.GradientDirection));
            _tile.BorderBrush = Widget.ShowTileBorder && S.BorderOpacity > 0.001
                ? ColorUtil.Solid(text, Math.Clamp(S.BorderOpacity, 0, 1) * Math.Max(0.35, S.TileOpacity))
                : Brushes.Transparent;
            _box.BaseSize = Widget.BaseSize;
            UpdateClip();

            Widget.ApplySettings();
            ApplyZOrder();
            UpdateChrome();
        }

        private void UpdateClip()
        {
            double r = Math.Clamp(S.CornerRadius, 0, 40);
            var g = new RectangleGeometry(new Rect(0, 0, _tile.ActualWidth, _tile.ActualHeight), r, r);
            g.Freeze();
            _tile.Clip = g;
        }

        /// <summary>Re-aplica só o fundo (ex.: imagem trocada).</summary>
        public void RefreshBackground()
        {
            var theme = ThemeService.ForWidget(Id);
            _tile.Background = Widget.TileBackground(ColorUtil.Gradient(theme, S.TileOpacity, S.GradientDirection));
        }

        public void UpdateChrome()
        {
            bool edit = WidgetManager.EditMode;
            _outline.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
            _label.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
            _grip.Visibility = S.Locked ? Visibility.Collapsed : Visibility.Visible;
            _grip.Opacity = edit ? 1 : IsMouseOver ? 0.75 : 0;
        }

        // ================== Layout ==================

        public WidgetLayout Layout => S.Layout(Id);

        public Rect Bounds => new Rect(Left, Top, Width, Height);

        public void LoadLayout()
        {
            var l = Layout;
            var def = Widget.DefaultSize;
            var min = Widget.MinSize;
            Width = Math.Max(min.Width, l.Width > 0 ? l.Width : def.Width);
            Height = Math.Max(min.Height, l.Height > 0 ? l.Height : def.Height);
            Left = l.Left;
            Top = l.Top;
        }

        public void MoveTo(double left, double top, double width = 0, double height = 0)
        {
            if (width > 0) Width = Math.Max(Widget.MinSize.Width, width);
            if (height > 0) Height = Math.Max(Widget.MinSize.Height, height);
            Left = left;
            Top = top;
            SaveLayout();
        }

        public void SaveLayout()
        {
            var l = Layout;
            l.Left = Math.Round(Left);
            l.Top = Math.Round(Top);
            l.Width = Math.Round(Width);
            l.Height = Math.Round(Height);
            SettingsService.SaveSoon();
        }

        private double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

        private Point CursorDip()
        {
            NativeMethods.GetCursorPos(out var p);
            double d = DpiScale;
            return new Point(p.X / d, p.Y / d);
        }

        // ---------- mover ----------

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            Dragged = false;
            _maybeDrag = !S.Locked && !IsInteractive(e.OriginalSource as DependencyObject);
            if (!_maybeDrag) return;
            _downCursor = CursorDip();
            _startLeft = Left;
            _startTop = Top;
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);
            if (!_maybeDrag) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }

            var p = CursorDip();
            double dx = p.X - _downCursor.X, dy = p.Y - _downCursor.Y;
            if (!_dragging)
            {
                if (Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;
                _dragging = true;
                Dragged = true;
                CaptureMouse();
            }

            var snapped = WidgetManager.SnapPosition(this, _startLeft + dx, _startTop + dy, Width, Height);
            Left = snapped.X;
            Top = snapped.Y;
            e.Handled = true;
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);
            if (_dragging) e.Handled = true; // não dispara o clique do conteúdo ao soltar um arraste
            EndDrag();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (_dragging) EndDrag();
        }

        private void EndDrag()
        {
            _maybeDrag = false;
            if (!_dragging) return;
            _dragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            SaveLayout();
        }

        private static bool IsInteractive(DependencyObject d)
        {
            while (d != null)
            {
                if (d is TextBoxBase || d is ButtonBase || d is ScrollBar || d is Thumb || d is Slider) return true;
                if (d is FrameworkElement fe && fe.Tag as string == "nodrag") return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        // ---------- redimensionar ----------

        private void Grip_DragStarted(object sender, DragStartedEventArgs e)
        {
            Dragged = true;
            _resizeCursor = CursorDip();
            _startW = Width;
            _startH = Height;
        }

        private void Grip_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var p = CursorDip();
            double w = _startW + (p.X - _resizeCursor.X);
            double h = _startH + (p.Y - _resizeCursor.Y);

            // Shift mantém a proporção
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _startH > 0)
            {
                double ratio = _startW / _startH;
                h = w / ratio;
            }

            var size = WidgetManager.SnapSize(this, w, h);
            Width = Math.Max(Widget.MinSize.Width, size.Width);
            Height = Math.Max(Widget.MinSize.Height, size.Height);
        }

        /// <summary>Ctrl + roda do mouse amplia/reduz este widget mantendo a proporção.</summary>
        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && !S.Locked)
            {
                double f = e.Delta > 0 ? 1.06 : 1 / 1.06;
                double w = Math.Clamp(Width * f, Widget.MinSize.Width, 2400);
                Height = Math.Max(Widget.MinSize.Height, Math.Round(Height * (w / Width)));
                Width = Math.Round(w);
                SaveLayout();
                e.Handled = true;
                return;
            }
            base.OnPreviewMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape && WidgetManager.EditMode) WidgetManager.EditMode = false;
        }

        // ================== Menu ==================

        private void BuildMenu()
        {
            var m = ContextMenu;
            m.Items.Clear();

            var extra = Widget.MenuItems().ToList();
            foreach (var it in extra) m.Items.Add(it);
            if (extra.Count > 0) m.Items.Add(new Separator());

            var themeMenu = new MenuItem { Header = "Tema deste widget" };
            var current = Layout.Theme ?? "";
            var global = new MenuItem { Header = "Seguir o tema global", IsCheckable = true, IsChecked = current.Length == 0 };
            global.Click += (s, e) => WidgetManager.SetWidgetTheme(Id, "");
            themeMenu.Items.Add(global);
            themeMenu.Items.Add(new Separator());
            foreach (var t in ThemeService.AllThemes)
            {
                var name = t.Name;
                var mi = new MenuItem
                {
                    Header = name,
                    IsCheckable = true,
                    IsChecked = string.Equals(current, name, StringComparison.OrdinalIgnoreCase),
                    Icon = new Border
                    {
                        Width = 14, Height = 14, CornerRadius = new CornerRadius(4),
                        Background = ColorUtil.Gradient(t, 1, "Diagonal")
                    }
                };
                mi.Click += (s, e) => WidgetManager.SetWidgetTheme(Id, name);
                themeMenu.Items.Add(mi);
            }
            m.Items.Add(themeMenu);

            var edit = new MenuItem { Header = "Organizar widgets (modo de edição)", IsCheckable = true, IsChecked = WidgetManager.EditMode };
            edit.Click += (s, e) => WidgetManager.EditMode = edit.IsChecked;
            m.Items.Add(edit);

            var arrange = new MenuItem { Header = "Alinhar todos automaticamente" };
            arrange.Click += (s, e) => WidgetManager.AutoArrange();
            m.Items.Add(arrange);

            var lockItem = new MenuItem { Header = "Travar posição e tamanho", IsCheckable = true, IsChecked = S.Locked };
            lockItem.Click += (s, e) => WidgetManager.SetLocked(lockItem.IsChecked);
            m.Items.Add(lockItem);

            var hide = new MenuItem { Header = "Ocultar este widget" };
            hide.Click += (s, e) => WidgetManager.SetVisible(Id, false);
            m.Items.Add(hide);

            m.Items.Add(new Separator());
            var settings = new MenuItem { Header = "Configurações…" };
            settings.Click += (s, e) => App.Instance.OpenSettings();
            m.Items.Add(settings);
            var exit = new MenuItem { Header = "Sair" };
            exit.Click += (s, e) => App.Instance.ExitApp();
            m.Items.Add(exit);
        }
    }
}
