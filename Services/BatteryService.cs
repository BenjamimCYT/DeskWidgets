using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeskWidgets.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Power;
using Windows.Foundation;
using Windows.Gaming.Input;
using Windows.Storage.Streams;
using Windows.System.Power;

namespace DeskWidgets.Services
{
    /// <summary>
    /// Lê a bateria de dispositivos conectados usando só APIs do Windows:
    ///  1) Propriedade de bateria Bluetooth do PnP (a mesma que aparece em Configurações > Bluetooth)
    ///  2) Serviço GATT "Battery" em dispositivos Bluetooth LE conectados (fallback)
    ///  3) Baterias HID / do sistema (alguns receptores 2.4 GHz e o próprio notebook)
    ///  4) Controles sem fio (Xbox, etc.) via Windows.Gaming.Input
    /// Receptores 2.4 GHz com protocolo proprietário (Logitech Lightspeed/Bolt, Razer HyperSpeed...)
    /// não expõem a bateria ao Windows; esses só funcionam com o SDK do fabricante.
    /// </summary>
    public static class BatteryService
    {
        private const string BtBatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2"; // DEVPKEY_Bluetooth_Battery
        private const string ContainerKey = "System.Devices.ContainerId";
        private const string AepConnectedKey = "System.Devices.Aep.IsConnected";
        private const string AepContainerKey = "System.Devices.Aep.ContainerId";

        public const string GlyphHeadphone = "\uE7F6";
        public const string GlyphMouse = "\uE962";
        public const string GlyphKeyboard = "\uE765";
        public const string GlyphGamepad = "\uE7FC";
        public const string GlyphPhone = "\uE8EA";
        public const string GlyphLaptop = "\uE7F8";
        public const string GlyphPen = "\uEDC6";
        public const string GlyphBluetooth = "\uE702";

        private sealed class BtEndpoint
        {
            public string Id;
            public string Name;
            public Guid Container;
            public bool Connected;
            public bool IsLE;
        }

        /// <summary>Avisa quando um controle é conectado/desconectado (para atualizar sem esperar o timer).</summary>
        public static void Init(Action changed)
        {
            try
            {
                RawGameController.RawGameControllerAdded += (s, c) => changed();
                RawGameController.RawGameControllerRemoved += (s, c) => changed();
                _ = RawGameController.RawGameControllers.Count; // inicializa a lista
            }
            catch (Exception ex) { SettingsService.Log(ex); }
        }

        public static async Task<List<DeviceBattery>> GetAllAsync()
        {
            var list = new List<DeviceBattery>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenContainers = new HashSet<Guid>();

            List<BtEndpoint> endpoints = new List<BtEndpoint>();
            try { endpoints = await GetBluetoothEndpointsAsync(); } catch (Exception ex) { SettingsService.Log(ex); }

            try { await AddPnpBluetoothAsync(list, endpoints, seenNames, seenContainers); } catch (Exception ex) { SettingsService.Log(ex); }
            try { await AddBleGattAsync(list, endpoints, seenNames, seenContainers); } catch (Exception ex) { SettingsService.Log(ex); }
            try { await AddSystemBatteriesAsync(list, seenNames); } catch (Exception ex) { SettingsService.Log(ex); }
            try { AddGameControllers(list, seenNames); } catch (Exception ex) { SettingsService.Log(ex); }

            return list.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // ---------- Bluetooth: estado de conexão por "container" ----------
        private static async Task<List<BtEndpoint>> GetBluetoothEndpointsAsync()
        {
            var result = new List<BtEndpoint>();
            string[] props = { AepConnectedKey, AepContainerKey };

            var selectors = new List<(string sel, bool le)>();
            try { selectors.Add((BluetoothDevice.GetDeviceSelectorFromPairingState(true), false)); } catch { }
            try { selectors.Add((BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), true)); } catch { }

            foreach (var (sel, le) in selectors)
            {
                DeviceInformationCollection items;
                try { items = await DeviceInformation.FindAllAsync(sel, props, DeviceInformationKind.AssociationEndpoint); }
                catch { continue; }

                foreach (var d in items)
                {
                    var ep = new BtEndpoint { Id = d.Id, Name = d.Name, IsLE = le };
                    if (d.Properties.TryGetValue(AepConnectedKey, out var c) && c is bool b) ep.Connected = b;
                    if (d.Properties.TryGetValue(AepContainerKey, out var g) && g is Guid gid) ep.Container = gid;
                    result.Add(ep);
                }
            }
            return result;
        }

        // ---------- 1) Propriedade de bateria Bluetooth (clássico e muitos BLE) ----------
        private static async Task AddPnpBluetoothAsync(List<DeviceBattery> list, List<BtEndpoint> endpoints,
                                                       HashSet<string> seenNames, HashSet<Guid> seenContainers)
        {
            string[] props = { BtBatteryKey, ContainerKey };
            DeviceInformationCollection devs = null;

            // Filtra só nós Bluetooth (BTHENUM / BTHLE...) para não varrer todos os dispositivos do PC
            try
            {
                devs = await DeviceInformation.FindAllAsync("System.Devices.DeviceInstanceId:~<\"BTH\"", props, DeviceInformationKind.Device);
            }
            catch { }
            if (devs == null || devs.Count == 0)
                devs = await DeviceInformation.FindAllAsync(string.Empty, props, DeviceInformationKind.Device);

            foreach (var d in devs)
            {
                if (!d.Properties.TryGetValue(BtBatteryKey, out var raw) || raw == null) continue;

                int level;
                try { level = Convert.ToInt32(raw); } catch { continue; }
                if (level < 0 || level > 100) continue;

                Guid container = d.Properties.TryGetValue(ContainerKey, out var cg) && cg is Guid g ? g : Guid.Empty;
                var same = container == Guid.Empty ? new List<BtEndpoint>() : endpoints.Where(e => e.Container == container).ToList();

                // Pareado mas desconectado: o Windows guarda o último valor; não mostramos.
                if (same.Count > 0 && !same.Any(e => e.Connected)) continue;
                if (container != Guid.Empty && !seenContainers.Add(container)) continue;

                string name = same.Select(e => e.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? CleanName(d.Name);
                if (!seenNames.Add(name)) continue;

                list.Add(Make("bt", name, level));
            }
        }

        // ---------- 2) GATT Battery Service (BLE conectados que não apareceram acima) ----------
        private static async Task AddBleGattAsync(List<DeviceBattery> list, List<BtEndpoint> endpoints,
                                                  HashSet<string> seenNames, HashSet<Guid> seenContainers)
        {
            foreach (var ep in endpoints.Where(e => e.IsLE && e.Connected))
            {
                if (ep.Container != Guid.Empty && seenContainers.Contains(ep.Container)) continue;
                if (string.IsNullOrWhiteSpace(ep.Name) || seenNames.Contains(ep.Name)) continue;

                int? level = await ReadGattBatteryAsync(ep.Id);
                if (level == null) continue;

                if (ep.Container != Guid.Empty) seenContainers.Add(ep.Container);
                seenNames.Add(ep.Name);
                list.Add(Make("ble", ep.Name, level.Value));
            }
        }

        private static async Task<int?> ReadGattBatteryAsync(string id)
        {
            var dev = await WithTimeout(BluetoothLEDevice.FromIdAsync(id), 4000);
            if (dev == null) return null;
            try
            {
                // Cached = não acorda o dispositivo só para descobrir serviços
                var svcs = await WithTimeout(dev.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached), 4000);
                if (svcs == null || svcs.Status != GattCommunicationStatus.Success || svcs.Services.Count == 0) return null;
                try
                {
                    var chars = await WithTimeout(
                        svcs.Services[0].GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached), 4000);
                    if (chars == null || chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0) return null;

                    var read = await WithTimeout(chars.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached), 4000);
                    if (read == null || read.Status != GattCommunicationStatus.Success || read.Value == null || read.Value.Length < 1) return null;

                    using var reader = DataReader.FromBuffer(read.Value);
                    int v = reader.ReadByte();
                    return v <= 100 ? v : (int?)null;
                }
                finally
                {
                    foreach (var s in svcs.Services) s.Dispose();
                }
            }
            finally
            {
                dev.Dispose();
            }
        }

        // ---------- 3) Baterias do sistema / HID (inclui alguns receptores 2.4 GHz) ----------
        private static async Task AddSystemBatteriesAsync(List<DeviceBattery> list, HashSet<string> seenNames)
        {
            var devs = await DeviceInformation.FindAllAsync(Battery.GetDeviceSelector());
            foreach (var d in devs)
            {
                Battery bat = null;
                try { bat = await Battery.FromIdAsync(d.Id); } catch { }
                if (bat == null) continue;

                var r = bat.GetReport();
                if (r.Status == BatteryStatus.NotPresent) continue;

                int? full = r.FullChargeCapacityInMilliwattHours;
                int? rem = r.RemainingCapacityInMilliwattHours;
                if (full == null || rem == null || full.Value <= 0) continue;

                int level = (int)Math.Round(Math.Clamp(rem.Value * 100.0 / full.Value, 0, 100));
                string raw = d.Name ?? "";
                bool isPc = raw.IndexOf("ACPI", StringComparison.OrdinalIgnoreCase) >= 0
                            || raw.IndexOf("Control Method", StringComparison.OrdinalIgnoreCase) >= 0
                            || raw.IndexOf("método de controle", StringComparison.OrdinalIgnoreCase) >= 0;

                string name = isPc ? "Este PC" : CleanName(raw);
                string unique = name;
                for (int i = 2; seenNames.Contains(unique); i++) unique = name + " " + i;
                seenNames.Add(unique);

                list.Add(new DeviceBattery
                {
                    Key = "sys:" + d.Id,
                    Name = unique,
                    Level = level,
                    Glyph = isPc ? GlyphLaptop : GuessGlyph(unique),
                    Source = "sys"
                });
            }
        }

        // ---------- 4) Controles sem fio ----------
        private static void AddGameControllers(List<DeviceBattery> list, HashSet<string> seenNames)
        {
            foreach (var c in RawGameController.RawGameControllers)
            {
                if (!c.IsWireless) continue;
                var rep = c.TryGetBatteryReport();
                if (rep == null) continue;

                int? full = rep.FullChargeCapacityInMilliwattHours;
                int? rem = rep.RemainingCapacityInMilliwattHours;
                if (full == null || rem == null || full.Value <= 0) continue;

                string name = string.IsNullOrWhiteSpace(c.DisplayName) ? "Controle" : c.DisplayName;
                if (!seenNames.Add(name)) continue;

                list.Add(new DeviceBattery
                {
                    Key = "pad:" + c.NonRoamableId,
                    Name = name,
                    Level = (int)Math.Round(Math.Clamp(rem.Value * 100.0 / full.Value, 0, 100)),
                    Glyph = GlyphGamepad,
                    Source = "pad"
                });
            }
        }

        // ---------- utilitários ----------
        private static DeviceBattery Make(string source, string name, int level) => new DeviceBattery
        {
            Key = source + ":" + name,
            Name = name,
            Level = level,
            Glyph = GuessGlyph(name),
            Source = source
        };

        private static async Task<T> WithTimeout<T>(IAsyncOperation<T> op, int ms)
        {
            using var cts = new CancellationTokenSource(ms);
            try { return await op.AsTask(cts.Token); }
            catch { return default; }
        }

        private static string CleanName(string n)
        {
            if (string.IsNullOrWhiteSpace(n)) return "Dispositivo";
            string[] suffixes = { " Hands-Free AG Audio", " Hands-Free AG", " Hands-Free", " Avrcp Transport", " Stereo", " Audio" };
            foreach (var s in suffixes)
                if (n.EndsWith(s, StringComparison.OrdinalIgnoreCase)) return n.Substring(0, n.Length - s.Length).Trim();
            return n.Trim();
        }

        public static string GuessGlyph(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            bool Has(params string[] w) => w.Any(n.Contains);

            if (Has("controller", "controle", "gamepad", "xbox", "dualsense", "dualshock", "joy-con", "8bitdo")) return GlyphGamepad;
            if (Has("mouse", "mx master", "mx anywhere", "trackpad")) return GlyphMouse;
            if (Has("keyboard", "teclado", "keys", "mx mechanical")) return GlyphKeyboard;
            if (Has("buds", "airpods", "fone", "headset", "headphone", "earbud", "earphone", "wh-", "wf-", "jbl", "tws", "soundcore", "qcy", "redmi buds")) return GlyphHeadphone;
            if (Has("pen", "caneta", "stylus")) return GlyphPen;
            if (Has("phone", "celular", "galaxy", "iphone", "pixel", "redmi", "moto")) return GlyphPhone;
            return GlyphBluetooth;
        }
    }
}
