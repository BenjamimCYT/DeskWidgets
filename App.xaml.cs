using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DeskWidgets.Helpers;
using DeskWidgets.Services;
using DeskWidgets.Widgets;

namespace DeskWidgets
{
    public partial class App : Application
    {
        private Mutex _mutex;
        private EventWaitHandle _showEvent;
        private const string ShowEventName = @"Local\DeskWidgets.Show";
        private const string AckEventName = @"Local\DeskWidgets.ShowAck";
        private EventWaitHandle _ackEvent;
        private bool _starting = true;
        private System.Windows.Forms.NotifyIcon _tray;
        private SettingsWindow _settings;

        public static App Instance => (App)Current;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, @"Local\DeskWidgets.SingleInstance", out bool isFirst);
            if (!isFirst && !TakeOverFromRunningInstance())
            {
                _mutex.Dispose();
                _mutex = null;
                Shutdown();
                return;
            }

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _ackEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AckEventName);
            var waiter = new Thread(() =>
            {
                while (true)
                {
                    _showEvent.WaitOne();
                    _ackEvent.Set(); // "estou vivo": a nova instância pode fechar tranquila
                    Dispatcher.BeginInvoke(new Action(() => { WidgetManager.BringBackAll(); OpenSettings(); }));
                }
            }, 64 * 1024) { IsBackground = true, Name = "show-signal" };
            waiter.Start();

            base.OnStartup(e);

            DispatcherUnhandledException += (s, a) =>
            {
                SettingsService.Log(a.Exception);
                a.Handled = true;
                if (_starting) ShowStartupError(a.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, a) =>
            {
                if (a.ExceptionObject is Exception ex) { SettingsService.Log(ex); ShowStartupError(ex); }
            };
            TaskScheduler.UnobservedTaskException += (s, a) => { SettingsService.Log(a.Exception); a.SetObserved(); };

            try
            {
                ResourceSaver.Apply();
                SettingsService.Load();

                // A bandeja vem primeiro: mesmo que um widget falhe, o app continua acessível
                CreateTray();
                WidgetManager.Init();

                // Checklist sincronizada com o Todoist (se configurado). Não reordena enquanto você digita.
                TodoistSync.Init(() => WidgetManager.Get<NotesWidget>()?.IsEditing != true);

                var trim = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(12) };
                trim.Tick += (s, a) => { trim.Stop(); ResourceSaver.Trim(); };
                trim.Start();

                // Mantém atalhos apontando para o .exe atual (caso ele tenha mudado de pasta)
                if (SettingsService.Current.StartMenuShortcut) ShortcutService.Set(true);
                if (StartupService.IsEnabled()) StartupService.Set(true);

                if (WidgetManager.Hosts.Count == 0 || WidgetManager.FailedWidgets.Count > 0)
                    Notify("DeskWidgets", WidgetManager.FailedWidgets.Count > 0
                        ? "Alguns widgets não carregaram: " + string.Join(", ", WidgetManager.FailedWidgets) + ". Detalhes em %AppData%\\DeskWidgets\\erros.log"
                        : "Nenhum widget carregou. Detalhes em %AppData%\\DeskWidgets\\erros.log");
            }
            catch (Exception ex)
            {
                SettingsService.Log(ex);
                ShowStartupError(ex);
            }
            finally
            {
                _starting = false;
            }
        }

        /// <summary>
        /// Outra cópia já está aberta. Se for uma versão antiga ou se não responder (travada),
        /// pergunta se pode fechá-la e assume o lugar. Retorna true se esta instância deve continuar.
        /// </summary>
        private bool TakeOverFromRunningInstance()
        {
            var me = Process.GetCurrentProcess();
            var myVersion = typeof(App).Assembly.GetName().Version ?? new Version(0, 0);
            var others = Process.GetProcessesByName(me.ProcessName).Where(p => p.Id != me.Id).ToList();

            bool older = others.Any(p =>
            {
                try
                {
                    var fv = FileVersionInfo.GetVersionInfo(p.MainModule.FileName);
                    return new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart) < new Version(myVersion.Major, myVersion.Minor, Math.Max(0, myVersion.Build));
                }
                catch { return false; }
            });

            bool responded = false;
            if (!older)
            {
                // Mesma versão: pede para ela mostrar as configurações e espera confirmar que está viva
                try
                {
                    using var ack = new EventWaitHandle(false, EventResetMode.AutoReset, AckEventName);
                    using var ev = EventWaitHandle.OpenExisting(ShowEventName);
                    ev.Set();
                    responded = ack.WaitOne(TimeSpan.FromSeconds(3));
                }
                catch { }
                if (responded) return false;
            }

            string msg = older
                ? "Uma versão anterior do DeskWidgets está aberta (ícone na bandeja).\n\nFechar a versão antiga e abrir esta?"
                : "O DeskWidgets já está aberto, mas não respondeu.\n\nFechar a cópia aberta e iniciar de novo?";
            var r = MessageBox.Show(msg, "DeskWidgets", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return false;

            foreach (var p in others)
            {
                try { p.Kill(); p.WaitForExit(5000); } catch (Exception ex) { SettingsService.Log(ex); }
            }
            try { return _mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { return true; } // a cópia antiga foi fechada segurando o mutex: agora é nosso
        }

        private bool _errorShown;

        private void ShowStartupError(Exception ex)
        {
            if (_errorShown) return;
            _errorShown = true;
            try
            {
                string text = "O DeskWidgets encontrou um erro ao iniciar:\n\n" + ex.GetType().Name + ": " + ex.Message +
                              "\n\nO texto completo foi copiado para a área de transferência e salvo em %AppData%\\DeskWidgets\\erros.log." +
                              "\nCole na conversa para eu corrigir.";
                try { Clipboard.SetText(ex.ToString()); } catch { }
                MessageBox.Show(text, "DeskWidgets", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
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
            menu.Items.Add("Mostrar widgets", null, (s, a) => WidgetManager.BringBackAll());
            menu.Items.Add("Nova nota", null, (s, a) =>
            {
                WidgetManager.SetVisible("notes", true);
                WidgetManager.Get<NotesWidget>()?.FocusNew();
            });

            var editItem = new System.Windows.Forms.ToolStripMenuItem("Organizar widgets (modo de edição)") { CheckOnClick = true };
            editItem.Click += (s, a) => WidgetManager.EditMode = editItem.Checked;
            menu.Items.Add(editItem);
            menu.Items.Add("Alinhar todos automaticamente", null, (s, a) => WidgetManager.AutoArrange());

            var lockItem = new System.Windows.Forms.ToolStripMenuItem("Travar posição e tamanho") { CheckOnClick = true };
            lockItem.Click += (s, a) => WidgetManager.SetLocked(lockItem.Checked);
            menu.Items.Add(lockItem);

            menu.Items.Add("Atualizar dados", null, (s, a) => WidgetManager.RefreshAll());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Sair", null, (s, a) => ExitApp());
            menu.Opening += (s, a) =>
            {
                lockItem.Checked = SettingsService.Current.Locked;
                editItem.Checked = WidgetManager.EditMode;
            };

            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += (s, a) =>
            {
                if (a.Button == System.Windows.Forms.MouseButtons.Left) WidgetManager.BringBackAll();
            };
        }

        /// <summary>Aviso na bandeja (pomodoro etc.).</summary>
        public void Notify(string title, string text)
        {
            try { _tray?.ShowBalloonTip(5000, title, text, System.Windows.Forms.ToolTipIcon.None); }
            catch (Exception ex) { SettingsService.Log(ex); }
        }

        public void OpenSettings()
        {
            try { OpenSettingsCore(); }
            catch (Exception ex)
            {
                SettingsService.Log(ex);
                _settings = null;
                try { Clipboard.SetText(ex.ToString()); } catch { }
                MessageBox.Show("Não foi possível abrir as configurações:\n\n" + ex.GetType().Name + ": " +
                    (ex.InnerException?.Message ?? ex.Message) +
                    "\n\nO detalhe foi copiado para a área de transferência. Cole na conversa para eu corrigir.",
                    "DeskWidgets", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSettingsCore()
        {
            if (_settings == null)
            {
                _settings = new SettingsWindow();
                _settings.Closed += (s, e) => _settings = null;
                _settings.Show();
            }
            if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
            _settings.Activate();
        }

        public void ExitApp()
        {
            WidgetManager.SaveAll();
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
