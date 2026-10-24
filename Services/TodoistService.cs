using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DeskWidgets.Services
{
    public class TodoistProject
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsInbox { get; set; }
        public string Display => IsInbox ? "Entrada (Inbox)" : Name;
    }

    public class TodoistItem
    {
        public string Id;
        public string Content;
        public string ProjectId;
        public bool Checked;
        public bool IsDeleted;
        public int ChildOrder;
    }

    public class TodoistSyncResult
    {
        public string SyncToken;
        public bool FullSync;
        public Dictionary<string, string> TempIdMapping = new Dictionary<string, string>();
        public Dictionary<string, string> CommandErrors = new Dictionary<string, string>(); // uuid → erro (ausente = ok)
        public List<TodoistItem> Items = new List<TodoistItem>();
        public List<TodoistProject> Projects = new List<TodoistProject>();
    }

    public class TodoistAuthException : Exception
    {
        public TodoistAuthException() : base("Token do Todoist inválido ou revogado.") { }
    }

    /// <summary>
    /// Usa o endpoint /sync da API v1 (o mesmo dos apps oficiais): uma única requisição envia
    /// as mudanças locais e recebe só o que mudou desde a última vez (sync incremental).
    /// Funciona no plano gratuito.
    /// </summary>
    public static class TodoistService
    {
        private const string SyncUrl = "https://api.todoist.com/api/v1/sync";
        public const string TokenPageUrl = "https://todoist.com/app/settings/integrations/developer";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("DeskWidgets/1.0");
            return c;
        }

        public static async Task<List<TodoistProject>> GetProjectsAsync(string token)
        {
            var r = await SyncAsync(token, "*", "[\"projects\"]", null);
            return r.Projects
                .OrderByDescending(p => p.IsInbox)
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static async Task<TodoistSyncResult> SyncAsync(string token, string syncToken, string resourceTypesJson, string commandsJson)
        {
            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("sync_token", string.IsNullOrEmpty(syncToken) ? "*" : syncToken),
                new KeyValuePair<string, string>("resource_types", resourceTypesJson ?? "[\"items\"]")
            };
            if (!string.IsNullOrEmpty(commandsJson))
                form.Add(new KeyValuePair<string, string>("commands", commandsJson));

            using var req = new HttpRequestMessage(HttpMethod.Post, SyncUrl) { Content = new FormUrlEncodedContent(form) };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
                throw new TodoistAuthException();
            resp.EnsureSuccessStatusCode();

            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return Parse(json);
        }

        private static TodoistSyncResult Parse(string json)
        {
            var r = new TodoistSyncResult();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            r.SyncToken = Str(root, "sync_token");
            r.FullSync = Bool(root, "full_sync");

            if (root.TryGetProperty("temp_id_mapping", out var map) && map.ValueKind == JsonValueKind.Object)
                foreach (var p in map.EnumerateObject())
                    r.TempIdMapping[p.Name] = AsId(p.Value);

            if (root.TryGetProperty("sync_status", out var st) && st.ValueKind == JsonValueKind.Object)
                foreach (var p in st.EnumerateObject())
                    if (!(p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() == "ok"))
                        r.CommandErrors[p.Name] = p.Value.GetRawText();

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var el in items.EnumerateArray())
                    r.Items.Add(new TodoistItem
                    {
                        Id = GetId(el, "id"),
                        Content = Str(el, "content"),
                        ProjectId = GetId(el, "project_id"),
                        Checked = Bool(el, "checked"),
                        IsDeleted = Bool(el, "is_deleted"),
                        ChildOrder = Int(el, "child_order")
                    });

            if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Array)
                foreach (var el in projects.EnumerateArray())
                {
                    if (Bool(el, "is_deleted") || Bool(el, "is_archived")) continue;
                    r.Projects.Add(new TodoistProject
                    {
                        Id = GetId(el, "id"),
                        Name = Str(el, "name"),
                        IsInbox = Bool(el, "inbox_project")
                    });
                }

            return r;
        }

        // ---------- helpers de JSON tolerantes (string/número, bool/0-1) ----------
        private static string Str(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

        private static string GetId(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) ? AsId(v) : null;

        private static string AsId(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        };

        private static bool Bool(JsonElement el, string prop)
        {
            if (!el.TryGetProperty(prop, out var v)) return false;
            return v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number => v.TryGetInt32(out int i) && i != 0,
                _ => false
            };
        }

        private static int Int(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : 0;
    }

    /// <summary>Guarda o token criptografado com a conta do Windows (DPAPI): só o seu usuário consegue ler.</summary>
    public static class Secret
    {
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            byte[] data = Encoding.UTF8.GetBytes(plain);
            return Convert.ToBase64String(Transform(data, true));
        }

        public static string Unprotect(string protectedBase64)
        {
            if (string.IsNullOrEmpty(protectedBase64)) return null;
            try { return Encoding.UTF8.GetString(Transform(Convert.FromBase64String(protectedBase64), false)); }
            catch { return null; }
        }

        private static byte[] Transform(byte[] input, bool protect)
        {
            var inBlob = new DATA_BLOB { cbData = input.Length, pbData = Marshal.AllocHGlobal(input.Length) };
            var outBlob = new DATA_BLOB();
            try
            {
                Marshal.Copy(input, 0, inBlob.pbData, input.Length);
                bool ok = protect
                    ? CryptProtectData(ref inBlob, "DeskWidgets", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob)
                    : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob);
                if (!ok) throw new InvalidOperationException("DPAPI falhou: " + Marshal.GetLastWin32Error());

                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(inBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
