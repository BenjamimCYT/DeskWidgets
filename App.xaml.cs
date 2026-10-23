using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DeskWidgets.Helpers;
using DeskWidgets.Services;

namespace DeskWidgets
{
    public partial class App : Application
    {
        private Mutex _mutex;
        private EventWaitHandle _showEvent;
        private const string ShowEventName = @"Local\DeskWidgets.Show";
        private System.Windows.Forms.NotifyIcon _tray;
        private SettingsWindow _settings;

        public static App Instance => (App)Current;
        public MainWindow Widget { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, @"Local\DeskWidgets.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                // Já está aberto (ex.: clicou no menu Iniciar): pede à instância aberta para mostrar as configurações
                try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); } catch { }
                _mutex.Dispose();
                _mutex = null;
                Shutdown();
                return;
            }

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var waiter = new Thread(() =>
            {
                while (true)
                {
                    _showEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(() => { Widget?.BringBack(); OpenSettings(); }));
                }
            }, 64 * 1024) { IsBackground = true, Name = "show-signal" };
            waiter.Start();

            base.OnStartup(e);

            DispatcherUnhandledException += (s, a) => { SettingsService.Log(a.Exception); a.Handled = true; };
            TaskScheduler.UnobservedTaskException += (s, a) => { SettingsService.Log(a.Exception); a.SetObserved(); };

            ResourceSaver.Apply();
            SettingsService.Load();

            Widget = new MainWindow();
            Widget.Show();
            CreateTray();

            // Mantém atalhos apontando para o .exe atual (caso ele tenha mudado de pasta)
            if (SettingsService.Current.StartMenuShortcut) ShortcutService.Set(true);
            if (StartupService.IsEnabled()) StartupService.Set(true);
        }

        private void CreateTray()
        {
            System.Drawing.Icon icon;
            try
            {
                var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
                icon = new System.Drawing.Icon(stream);
            }
            catch
            {
                icon = System.Drawing.SystemIcons.Application;
            }

            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon,
                Text = "DeskWidgets",
                Visible = true
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Configurações…", null, (s, a) => OpenSettings());
            menu.Items.Add("Abrir notas", null, (s, a) => { Widget.BringBack(); Widget.OpenNotes(); });
            menu.Items.Add("Mostrar widgets", null, (s, a) => Widget.BringBack());

            var lockItem = new System.Windows.Forms.ToolStripMenuItem("Travar posição") { CheckOnClick = true };
            lockItem.Click += (s, a) =>
            {
                SettingsService.Current.Locked = lockItem.Checked;
                SettingsService.SaveSoon();
            };
            menu.Items.Add(lockItem);

            menu.Items.Add("Atualizar clima e bateria", null, (s, a) => Widget.RefreshAll());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Sair", null, (s, a) => ExitApp());
            menu.Opening += (s, a) => lockItem.Checked = SettingsService.Current.Locked;

            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += (s, a) =>
            {
                if (a.Button == System.Windows.Forms.MouseButtons.Left) Widget.BringBack();
            };
        }

        public void OpenSettings()
        {
            if (_settings == null)
            {
                _settings = new SettingsWindow(Widget);
                _settings.Closed += (s, e) => _settings = null;
                _settings.Show();
            }
            if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
            _settings.Activate();
        }

        public void ExitApp()
        {
            SettingsService.SaveNow();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
            catch { }
            base.OnExit(e);
        }
    }
}
