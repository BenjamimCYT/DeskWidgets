using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using DeskWidgets.Models;

namespace DeskWidgets.Services
{
    /// <summary>
    /// Mantém a checklist do widget igual a uma lista (projeto) do Todoist.
    ///
    /// Como funciona: cada nota guarda o id da tarefa no Todoist e o último texto/estado sincronizado.
    /// A cada sincronização, o que mudou localmente vira comando (adicionar, editar, concluir, reabrir,
    /// excluir) e, na mesma requisição, chegam as mudanças feitas no celular. Sem internet, as mudanças
    /// ficam guardadas e são enviadas depois.
    ///
    /// Custo: 1 requisição a cada 2 min (incremental: só o que mudou) + 1 alguns segundos após editar.
    /// </summary>
    public static class TodoistSync
    {
        private static AppSettings S => SettingsService.Current;

        private static DispatcherTimer _poll;
        private static DispatcherTimer _flush;
        private static string _syncToken = "*";
        private static readonly Dictionary<string, TodoistItem> Remote = new Dictionary<string, TodoistItem>();
        private static bool _busy, _again, _applying, _initialized;
        private static Func<bool> _canReorder = () => true;

        public static string LastStatus { get; private set; } = "";
        public static event Action<string> StatusChanged;

        public static string Token => Secret.Unprotect(S.TodoistToken);

        public static bool Enabled =>
            S.NotesSource == "todoist" && !string.IsNullOrEmpty(S.TodoistProjectId) && !string.IsNullOrEmpty(S.TodoistToken);

        public static void Init(Func<bool> canReorder)
        {
            if (_initialized) return;
            _initialized = true;
            _canReorder = canReorder ?? (() => true);

            _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(2) };
            _poll.Tick += (s, e) => SyncNow();

            _flush = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2.5) };
            _flush.Tick += (s, e) => { _flush.Stop(); SyncNow(); };

            SettingsService.Notes.CollectionChanged += OnCollectionChanged;
            SettingsService.NotesChanged += () => { if (Enabled && !_applying) ScheduleFlush(); };

            Restart();
        }

        /// <summary>Recomeça do zero (após conectar, trocar de lista ou desconectar).</summary>
        public static void Restart()
        {
            _syncToken = "*";
            Remote.Clear();
            if (!_initialized) return;

            if (Enabled)
            {
                _poll.Start();
                SyncNow();
            }
            else
            {
                _poll.Stop();
                _flush.Stop();
                SetStatus("");
            }
        }

        public static void ScheduleFlush()
        {
            if (!_initialized || !Enabled) return;
            _flush.Stop();
            _flush.Start();
        }

        /// <summary>Esvazia a checklist local sem apagar nada no Todoist.</summary>
        public static void ClearLocal()
        {
            _applying = true;
            try
            {
                foreach (var n in SettingsService.Notes.ToList()) SettingsService.Notes.Remove(n);
                S.TodoistPendingDeletes.Clear();
            }
            finally { _applying = false; }
        }

        /// <summary>Ao desconectar: as tarefas atuais continuam no widget como notas locais.</summary>
        public static void Detach()
        {
            foreach (var n in SettingsService.Notes)
            {
                n.RemoteId = null;
                n.SyncedText = null;
                n.SyncedDone = false;
                n.TempId = null;
                n.SyncFailed = false;
            }
            S.TodoistPendingDeletes.Clear();
        }

        private static void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_applying || !Enabled) return;
            if (e.Action != NotifyCollectionChangedAction.Remove && e.Action != NotifyCollectionChangedAction.Replace) return;
            if (e.OldItems == null) return;

            foreach (NoteItem n in e.OldItems)
            {
                // Itens concluídos já foram fechados no Todoist: "Limpar concluídos" só tira do widget.
                if (n.RemoteId != null && !n.Done && !S.TodoistPendingDeletes.Contains(n.RemoteId))
                    S.TodoistPendingDeletes.Add(n.RemoteId);
            }
            ScheduleFlush();
        }

        private sealed class Cmd
        {
            public string Type;
            public string Uuid = Guid.NewGuid().ToString();
            public string TempId;
            public Dictionary<string, object> Args;
            public Action<TodoistSyncResult, bool> OnDone;
        }

        public static async void SyncNow()
        {
            if (!Enabled) return;
            if (_busy) { _again = true; return; }
            _busy = true;

            string token = Token;
            if (string.IsNullOrEmpty(token))
            {
                SetStatus("Token do Todoist não encontrado. Conecte de novo nas configurações.");
                _busy = false;
                return;
            }

            try
            {
                var cmds = BuildCommands();
                string commandsJson = cmds.Count == 0 ? null : JsonSerializer.Serialize(cmds.Select(c =>
                {
                    var d = new Dictionary<string, object> { ["type"] = c.Type, ["uuid"] = c.Uuid, ["args"] = c.Args };
                    if (c.TempId != null) d["temp_id"] = c.TempId;
                    return d;
                }).ToList());

                var result = await TodoistService.SyncAsync(token, _syncToken, "[\"items\"]", commandsJson);

                foreach (var c in cmds)
                {
                    bool ok = !result.CommandErrors.ContainsKey(c.Uuid);
                    if (!ok) SettingsService.Log(new Exception($"Todoist {c.Type}: {result.CommandErrors[c.Uuid]}"));
                    c.OnDone?.Invoke(result, ok);
                }

                if (result.FullSync) Remote.Clear();
                foreach (var it in result.Items)
                {
                    if (string.IsNullOrEmpty(it.Id)) continue;
                    Remote[it.Id] = it;
                }
                if (!string.IsNullOrEmpty(result.SyncToken)) _syncToken = result.SyncToken;

                Reconcile();
                SettingsService.SaveSoon();
                SetStatus($"Sincronizado com “{S.TodoistProjectName}” às {DateTime.Now:HH:mm}");
            }
            catch (TodoistAuthException)
            {
                SetStatus("O Todoist recusou o token. Gere um novo e conecte de novo.");
            }
            catch (Exception ex)
            {
                SettingsService.Log(ex);
                SetStatus("Sem conexão com o Todoist. As mudanças ficam guardadas e vão na próxima tentativa.");
            }
            finally
            {
                _busy = false;
                if (_again) { _again = false; ScheduleFlush(); }
            }
        }

        private static List<Cmd> BuildCommands()
        {
            var cmds = new List<Cmd>();
            string project = S.TodoistProjectId;

            foreach (var n in SettingsService.Notes.ToList())
            {
                var item = n;
                if (item.RemoteId == null)
                {
                    if (item.SyncFailed || string.IsNullOrWhiteSpace(item.Text)) continue;
                    item.TempId ??= Guid.NewGuid().ToString();
                    string text = item.Text.Trim();

                    cmds.Add(new Cmd
                    {
                        Type = "item_add",
                        TempId = item.TempId,
                        Args = new Dictionary<string, object> { ["content"] = text, ["project_id"] = project },
                        OnDone = (r, ok) =>
                        {
                            if (ok && r.TempIdMapping.TryGetValue(item.TempId, out var id))
                            {
                                item.RemoteId = id;
                                item.SyncedText = text;
                                item.SyncedDone = false;
                            }
                            else if (!ok)
                            {
                                item.SyncFailed = true; // ex.: limite de tarefas do plano; não fica tentando em loop
                            }
                        }
                    });

                    if (item.Done)
                    {
                        cmds.Add(new Cmd
                        {
                            Type = "item_close",
                            Args = new Dictionary<string, object> { ["id"] = item.TempId },
                            OnDone = (r, ok) => { if (ok) item.SyncedDone = true; }
                        });
                    }
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(item.Text) && item.Text != item.SyncedText)
                {
                    string text = item.Text;
                    cmds.Add(new Cmd
                    {
                        Type = "item_update",
                        Args = new Dictionary<string, object> { ["id"] = item.RemoteId, ["content"] = text },
                        OnDone = (r, ok) => item.SyncedText = text // em erro também: evita repetir para sempre
                    });
                }

                if (item.Done != item.SyncedDone)
                {
                    bool done = item.Done;
                    cmds.Add(new Cmd
                    {
                        Type = done ? "item_close" : "item_uncomplete",
                        Args = new Dictionary<string, object> { ["id"] = item.RemoteId },
                        OnDone = (r, ok) => item.SyncedDone = done
                    });
                }
            }

            foreach (var id in S.TodoistPendingDeletes.ToList())
            {
                string rid = id;
                cmds.Add(new Cmd
                {
                    Type = "item_delete",
                    Args = new Dictionary<string, object> { ["id"] = rid },
                    OnDone = (r, ok) => S.TodoistPendingDeletes.Remove(rid)
                });
            }

            return cmds;
        }

        /// <summary>Aplica no widget o que mudou no Todoist (celular, web...).</summary>
        private static void Reconcile()
        {
            string project = S.TodoistProjectId;
            var active = Remote.Values
                .Where(r => r.ProjectId == project && !r.Checked && !r.IsDeleted)
                .OrderBy(r => r.ChildOrder)
                .ToList();
            var activeIds = new HashSet<string>(active.Select(r => r.Id));
            var notes = SettingsService.Notes;

            _applying = true;
            try
            {
                // 1) Concluídas ou apagadas em outro lugar somem do widget
                foreach (var n in notes.Where(n => n.RemoteId != null && !activeIds.Contains(n.RemoteId)).ToList())
                {
                    bool pendingLocal = n.Text != n.SyncedText || n.Done != n.SyncedDone;
                    if (n.Done || pendingLocal) continue; // concluídas aqui ficam riscadas até "Limpar concluídos"
                    notes.Remove(n);
                }

                // 2) Atualiza textos e adiciona tarefas novas
                var byId = notes.Where(n => n.RemoteId != null)
                                .GroupBy(n => n.RemoteId)
                                .ToDictionary(g => g.Key, g => g.First());
                foreach (var r in active)
                {
                    if (byId.TryGetValue(r.Id, out var n))
                    {
                        if (n.Text == n.SyncedText && n.Text != r.Content)
                        {
                            n.Text = r.Content;
                            n.SyncedText = r.Content;
                        }
                        if (n.Done && n.SyncedDone) // reaberta no celular
                        {
                            n.Done = false;
                            n.SyncedDone = false;
                        }
                    }
                    else
                    {
                        notes.Add(new NoteItem { Text = r.Content, RemoteId = r.Id, SyncedText = r.Content, SyncedDone = false });
                    }
                }

                // 3) Mesma ordem do Todoist (não reordena enquanto você digita)
                if (_canReorder())
                {
                    var order = active.Select(r => notes.FirstOrDefault(n => n.RemoteId == r.Id)).Where(n => n != null).ToList();
                    order.AddRange(notes.Where(n => !order.Contains(n)).ToList());
                    for (int i = 0; i < order.Count; i++)
                    {
                        int cur = notes.IndexOf(order[i]);
                        if (cur != i && cur >= 0) notes.Move(cur, i);
                    }
                }
            }
            finally
            {
                _applying = false;
            }
        }

        private static void SetStatus(string s)
        {
            LastStatus = s;
            StatusChanged?.Invoke(s);
        }
    }
}
