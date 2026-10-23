using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeskWidgets.Models;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;

namespace DeskWidgets.Services
{
    /// <summary>
    /// Bateria de mouses 2.4 GHz que usam protocolo próprio do fabricante.
    /// O receptor tem uma interface HID "do fabricante" por onde o mouse envia pacotes espontâneos
    /// (por exemplo, quando a carga muda ou quando ele acorda). O app só ESCUTA essa interface:
    /// não envia nenhum comando ao mouse, então não há risco de mexer em DPI, luzes ou pareamento.
    ///
    /// Cada dispositivo escutado usa 1 thread parada em ReadFile (0% de CPU enquanto espera).
    /// </summary>
    public static class VendorHidBattery
    {
        private sealed class KnownDevice
        {
            public string Name;
            public ushort Vid;
            public ushort Pid;
            public int Interface;
            public byte[] Prefix;     // início do pacote de bateria (com o Report ID)
            public int LevelIndex;    // posição do byte com a porcentagem
            public string Glyph;

            public string Key => $"vhid:{Vid:x4}:{Pid:x4}";
            public string Match => $"VID_{Vid:X4}&PID_{Pid:X4}";
            public string InterfaceMatch => $"&MI_{Interface:X2}";
        }

        // Para adicionar outro mouse: VID/PID do receptor, interface e formato do pacote.
        private static readonly KnownDevice[] Known =
        {
            // Attack Shark X11 pelo receptor 2.4 GHz: pacote "03 55 40 01 XX", XX = 0..100
            // Fonte: engenharia reversa dos projetos OpenSharkX11 e attack-shark-x11-driver.
            new KnownDevice
            {
                Name = "Attack Shark X11",
                Vid = 0x1D57, Pid = 0xFA60, Interface = 2,
                Prefix = new byte[] { 0x03, 0x55, 0x40 },
                LevelIndex = 4,
                Glyph = BatteryService.GlyphMouse
            },
        };

        private const string HidInterfaceAqs =
            "System.Devices.InterfaceClassGuid:=\"{4D1E55B2-F16F-11CF-88CB-001111000030}\" AND " +
            "System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";

        private sealed class Listener
        {
            public KnownDevice Device;
            public volatile bool Alive = true;
        }

        private static readonly ConcurrentDictionary<string, Listener> Listeners =
            new ConcurrentDictionary<string, Listener>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, (int Level, DateTime When)> Live =
            new ConcurrentDictionary<string, (int Level, DateTime When)>();
        private static readonly ConcurrentDictionary<string, string> Status =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>(chave do dispositivo, porcentagem) — chamado na thread de leitura.</summary>
        public static event Action<string, int> LevelReceived;

        /// <summary>
        /// Garante que os receptores conhecidos conectados estão sendo escutados e adiciona-os à lista.
        /// Nível -1 = receptor conectado, mas o mouse ainda não informou a bateria.
        /// </summary>
        public static async Task AddAsync(List<DeviceBattery> list, HashSet<string> seenNames,
                                          IReadOnlyDictionary<string, int> persisted)
        {
            var infos = await DeviceInformation.FindAllAsync(HidInterfaceAqs);
            var present = new HashSet<string>();

            foreach (var info in infos)
            {
                string id = info.Id ?? "";
                string up = id.ToUpperInvariant();
                foreach (var dev in Known)
                {
                    if (!up.Contains(dev.Match)) continue;
                    present.Add(dev.Key);
                    if (!up.Contains(dev.InterfaceMatch)) continue;
                    if (Listeners.TryGetValue(id, out var l) && l.Alive) continue;
                    TryStart(id, dev);
                }
            }

            foreach (var dev in Known.Where(d => present.Contains(d.Key)))
            {
                int level = Live.TryGetValue(dev.Key, out var v) ? v.Level
                          : persisted != null && persisted.TryGetValue(dev.Key, out var p) ? p
                          : -1;
                seenNames.Add(dev.Name);
                list.Add(new DeviceBattery
                {
                    Key = dev.Key,
                    Name = dev.Name,
                    Level = level,
                    Glyph = dev.Glyph,
                    Source = "vhid"
                });
            }
        }

        private static void TryStart(string path, KnownDevice dev)
        {
            var h = Native.CreateFile(path, Native.GENERIC_READ, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                                      IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid)
            {
                Status[path] = "não abriu (erro " + Marshal.GetLastWin32Error() + ")";
                h.Dispose();
                return;
            }

            var (len, usagePage, usage) = ReadCaps(h);
            if (len <= 0)
            {
                Status[path] = $"sem relatórios de entrada (página 0x{usagePage:X4})";
                h.Dispose();
                return;
            }

            var listener = new Listener { Device = dev };
            Listeners[path] = listener;
            Status[path] = $"escutando (página 0x{usagePage:X4}, uso 0x{usage:X4}, {len} bytes)";

            var t = new Thread(() => ReadLoop(path, h, len, listener), 64 * 1024)
            {
                IsBackground = true,
                Name = "hid-bateria",
                Priority = ThreadPriority.BelowNormal
            };
            t.Start();
        }

        private static void ReadLoop(string path, SafeFileHandle h, int len, Listener l)
        {
            var buf = new byte[len];
            var dev = l.Device;
            try
            {
                while (true)
                {
                    if (!Native.ReadFile(h, buf, len, out int read, IntPtr.Zero) || read <= 0) break;
                    if (read <= dev.LevelIndex) continue;

                    bool match = true;
                    for (int i = 0; i < dev.Prefix.Length; i++)
                        if (buf[i] != dev.Prefix[i]) { match = false; break; }
                    if (!match) continue;

                    int level = buf[dev.LevelIndex];
                    if (level > 100) continue;

                    Live[dev.Key] = (level, DateTime.Now);
                    Status[path] = $"escutando; última leitura {level}% às {DateTime.Now:HH:mm}";
                    try { LevelReceived?.Invoke(dev.Key, level); } catch { }
                }
            }
            catch (Exception ex) { SettingsService.Log(ex); }
            finally
            {
                l.Alive = false;
                Listeners.TryRemove(path, out _);
                Status[path] = "desconectado";
                h.Dispose();
            }
        }

        private static (int len, ushort usagePage, ushort usage) ReadCaps(SafeFileHandle h)
        {
            if (!Native.HidD_GetPreparsedData(h, out IntPtr pp)) return (0, 0, 0);
            try
            {
                if (Native.HidP_GetCaps(pp, out var caps) != Native.HIDP_STATUS_SUCCESS) return (0, 0, 0);
                return (caps.InputReportByteLength, caps.UsagePage, caps.Usage);
            }
            finally
            {
                Native.HidD_FreePreparsedData(pp);
            }
        }

        /// <summary>Texto para o botão "Copiar diagnóstico" — ajuda a adicionar mouses novos.</summary>
        public static async Task<string> DiagnosticsAsync()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Interfaces HID (VID/PID/MI/Col → nome → status):");
            try
            {
                var infos = await DeviceInformation.FindAllAsync(HidInterfaceAqs);
                foreach (var info in infos.OrderBy(i => i.Id))
                {
                    string id = info.Id ?? "";
                    int a = id.IndexOf("HID#", StringComparison.OrdinalIgnoreCase);
                    if (a < 0) continue;
                    int b = id.IndexOf('#', a + 4);
                    string shortId = b > a ? id.Substring(a + 4, b - a - 4) : id;
                    if (!shortId.StartsWith("VID_", StringComparison.OrdinalIgnoreCase)) continue;

                    string status = Status.TryGetValue(id, out var s) ? s : "-";
                    if (status == "-") status = Probe(id);
                    sb.AppendLine($"{shortId} → {info.Name} → {status}");
                }
            }
            catch (Exception ex) { sb.AppendLine("erro ao listar: " + ex.Message); }
            return sb.ToString();
        }

        /// <summary>Só lê as capacidades da interface (não envia nada ao dispositivo).</summary>
        private static string Probe(string path)
        {
            using var h = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                                            IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) return "sem acesso";
            var (len, page, usage) = ReadCaps(h);
            return $"página 0x{page:X4}, uso 0x{usage:X4}, entrada {len} bytes";
        }

        private static class Native
        {
            public const uint GENERIC_READ = 0x80000000;
            public const uint FILE_SHARE_READ = 0x1;
            public const uint FILE_SHARE_WRITE = 0x2;
            public const uint OPEN_EXISTING = 3;
            public const int HIDP_STATUS_SUCCESS = 0x00110000;

            [StructLayout(LayoutKind.Sequential)]
            public struct HIDP_CAPS
            {
                public ushort Usage;
                public ushort UsagePage;
                public ushort InputReportByteLength;
                public ushort OutputReportByteLength;
                public ushort FeatureReportByteLength;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
                public ushort[] Reserved;
                public ushort NumberLinkCollectionNodes;
                public ushort NumberInputButtonCaps;
                public ushort NumberInputValueCaps;
                public ushort NumberInputDataIndices;
                public ushort NumberOutputButtonCaps;
                public ushort NumberOutputValueCaps;
                public ushort NumberOutputDataIndices;
                public ushort NumberFeatureButtonCaps;
                public ushort NumberFeatureValueCaps;
                public ushort NumberFeatureDataIndices;
            }

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
                IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int numberOfBytesToRead,
                out int numberOfBytesRead, IntPtr overlapped);

            [DllImport("hid.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.U1)]
            public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

            [DllImport("hid.dll")]
            [return: MarshalAs(UnmanagedType.U1)]
            public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

            [DllImport("hid.dll")]
            public static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);
        }
    }
}
