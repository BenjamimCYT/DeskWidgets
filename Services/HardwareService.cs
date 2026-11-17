using System;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskWidgets.Services
{
    public class HardwareSnapshot
    {
        public double? CpuTemp { get; set; }
        public string CpuTempSource { get; set; } = "";
        public double? GpuTemp { get; set; }
        public double? GpuLoad { get; set; }
        public string GpuName { get; set; } = "";
        public double? DiskTemp { get; set; }
        public double CpuLoad { get; set; }
        public double RamPercent { get; set; }
        public double RamUsedGb { get; set; }
        public double RamTotalGb { get; set; }
    }

    /// <summary>
    /// Leitura de temperaturas sem precisar de administrador:
    ///  • LibreHardwareMonitor / OpenHardwareMonitor abertos → sensores precisos de CPU, GPU e SSD (via WMI);
    ///  • GPU NVIDIA → direto pelo driver (nvml.dll);
    ///  • CPU → zona térmica do Windows (aproximação) quando não houver nada melhor.
    /// Uso de CPU e memória vêm direto do Windows.
    /// </summary>
    public static class HardwareService
    {
        private static readonly object Lock = new object();
        private static ulong _lastIdle, _lastKernel, _lastUser;
        private static DateTime _lhmRetry = DateTime.MinValue, _diskRetry = DateTime.MinValue;
        private static string _lhmNamespace;          // namespace que respondeu por último
        private static bool _nvmlTried, _nvmlOk;
        private static IntPtr _nvmlDevice;

        public static string LastSources { get; private set; } = "";

        public static HardwareSnapshot Read()
        {
            lock (Lock)
            {
                var s = new HardwareSnapshot();
                ReadCpuLoad(s);
                ReadMemory(s);
                var sources = new System.Collections.Generic.List<string>();

                if (ReadMonitorApp(s)) sources.Add(_lhmNamespace.Contains("Libre") ? "LibreHardwareMonitor" : "OpenHardwareMonitor");
                if (s.GpuTemp == null && ReadNvidia(s)) sources.Add("driver NVIDIA");
                if (s.CpuTemp == null && ReadThermalZone(s)) sources.Add("zona térmica do Windows");
                if (s.DiskTemp == null) ReadDiskReliability(s);

                LastSources = sources.Count == 0 ? "nenhum sensor de temperatura disponível" : string.Join(", ", sources);
                return s;
            }
        }

        // ================== CPU e memória ==================

        private static void ReadCpuLoad(HardwareSnapshot s)
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return;
            ulong i = ToU(idle), k = ToU(kernel), u = ToU(user);
            if (_lastKernel != 0)
            {
                ulong di = i - _lastIdle, dk = k - _lastKernel, du = u - _lastUser;
                ulong total = dk + du; // kernel já inclui idle
                if (total > 0) s.CpuLoad = Math.Clamp((total - di) * 100.0 / total, 0, 100);
            }
            _lastIdle = i; _lastKernel = k; _lastUser = u;
        }

        private static void ReadMemory(HardwareSnapshot s)
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m)) return;
            s.RamPercent = m.dwMemoryLoad;
            s.RamTotalGb = m.ullTotalPhys / 1073741824.0;
            s.RamUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        }

        // ================== LibreHardwareMonitor / OpenHardwareMonitor ==================

        private static bool ReadMonitorApp(HardwareSnapshot s)
        {
            if (DateTime.Now < _lhmRetry) return false;
            foreach (var ns in _lhmNamespace != null ? new[] { _lhmNamespace } : new[] { @"root\LibreHardwareMonitor", @"root\OpenHardwareMonitor" })
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(ns,
                        "SELECT Name, Identifier, SensorType, Value FROM Sensor WHERE SensorType='Temperature' OR SensorType='Load'");
                    searcher.Options.Timeout = TimeSpan.FromSeconds(3);
                    var rows = searcher.Get().Cast<ManagementBaseObject>().Select(o => new
                    {
                        Name = o["Name"] as string ?? "",
                        Id = (o["Identifier"] as string ?? "").ToLowerInvariant(),
                        Type = o["SensorType"] as string ?? "",
                        Value = Convert.ToDouble(o["Value"] ?? 0.0)
                    }).ToList();
                    if (rows.Count == 0) continue;

                    var temps = rows.Where(r => r.Type == "Temperature" && r.Value > 1 && r.Value < 130).ToList();

                    var cpu = temps.Where(r => r.Id.Contains("cpu")).ToList();
                    var cpuBest = cpu.FirstOrDefault(r => r.Name.Contains("Package")) ??
                                  cpu.FirstOrDefault(r => r.Name.Contains("Tctl") || r.Name.Contains("Tdie")) ??
                                  cpu.OrderByDescending(r => r.Value).FirstOrDefault();
                    if (cpuBest != null) { s.CpuTemp = cpuBest.Value; s.CpuTempSource = "sensor"; }

                    var gpu = temps.Where(r => r.Id.Contains("gpu")).ToList();
                    var gpuBest = gpu.FirstOrDefault(r => r.Name == "GPU Core") ?? gpu.OrderByDescending(r => r.Value).FirstOrDefault();
                    if (gpuBest != null) s.GpuTemp = gpuBest.Value;
                    var gpuLoad = rows.FirstOrDefault(r => r.Type == "Load" && r.Id.Contains("gpu") && r.Name == "GPU Core");
                    if (gpuLoad != null) s.GpuLoad = gpuLoad.Value;

                    var disk = temps.Where(r => r.Id.Contains("nvme") || r.Id.Contains("hdd") || r.Id.Contains("ssd") || r.Id.Contains("storage")).ToList();
                    if (disk.Count > 0) s.DiskTemp = disk.Max(r => r.Value);

                    _lhmNamespace = ns;
                    return cpuBest != null || gpuBest != null || disk.Count > 0;
                }
                catch (ManagementException) { /* namespace não existe: o app não está aberto */ }
                catch (Exception ex) { SettingsService.Log(ex); }
            }
            _lhmNamespace = null;
            _lhmRetry = DateTime.Now.AddSeconds(60);
            return false;
        }

        // ================== NVIDIA (nvml.dll vem com o driver) ==================

        private static bool ReadNvidia(HardwareSnapshot s)
        {
            try
            {
                if (!_nvmlTried)
                {
                    _nvmlTried = true;
                    if (nvmlInit_v2() == 0 && nvmlDeviceGetCount_v2(out uint count) == 0 && count > 0
                        && nvmlDeviceGetHandleByIndex_v2(0, out _nvmlDevice) == 0)
                        _nvmlOk = true;
                }
                if (!_nvmlOk) return false;

                if (nvmlDeviceGetTemperature(_nvmlDevice, 0, out uint t) == 0) s.GpuTemp = t;
                if (nvmlDeviceGetUtilizationRates(_nvmlDevice, out var u) == 0) s.GpuLoad = u.gpu;
                var name = new byte[96];
                if (nvmlDeviceGetName(_nvmlDevice, name, (uint)name.Length) == 0)
                    s.GpuName = Encoding.ASCII.GetString(name).TrimEnd('\0');
                return s.GpuTemp != null;
            }
            catch (DllNotFoundException) { _nvmlOk = false; return false; }
            catch (EntryPointNotFoundException) { _nvmlOk = false; return false; }
            catch (Exception ex) { SettingsService.Log(ex); _nvmlOk = false; return false; }
        }

        // ================== Zona térmica (sem administrador) ==================

        private static bool ReadThermalZone(HardwareSnapshot s)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\cimv2",
                    "SELECT HighPrecisionTemperature, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
                searcher.Options.Timeout = TimeSpan.FromSeconds(3);
                double best = double.NaN;
                foreach (var o in searcher.Get())
                {
                    double k = 0;
                    try { k = Convert.ToDouble(o["HighPrecisionTemperature"]) / 10.0; } catch { }
                    if (k <= 0) { try { k = Convert.ToDouble(o["Temperature"]); } catch { } }
                    double c = k - 273.15;
                    if (c > 5 && c < 125 && (double.IsNaN(best) || c > best)) best = c;
                }
                if (double.IsNaN(best)) return false;
                s.CpuTemp = best;
                s.CpuTempSource = "aprox.";
                return true;
            }
            catch (Exception) { return false; }
        }

        // ================== SSD/HD (às vezes exige administrador) ==================

        private static void ReadDiskReliability(HardwareSnapshot s)
        {
            if (DateTime.Now < _diskRetry) return;
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage",
                    "SELECT Temperature FROM MSFT_StorageReliabilityCounter");
                searcher.Options.Timeout = TimeSpan.FromSeconds(3);
                double best = 0;
                foreach (var o in searcher.Get())
                {
                    try { best = Math.Max(best, Convert.ToDouble(o["Temperature"])); } catch { }
                }
                if (best > 1 && best < 120) s.DiskTemp = best;
                else _diskRetry = DateTime.Now.AddMinutes(10);
            }
            catch { _diskRetry = DateTime.Now.AddMinutes(10); }
        }

        // ================== P/Invoke ==================

        private static ulong ToU(FILETIME f) => ((ulong)(uint)f.dwHighDateTime << 32) | (uint)f.dwLowDateTime;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME { public int dwLowDateTime; public int dwHighDateTime; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlUtilization { public uint gpu, memory; }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        [DllImport("nvml.dll")] private static extern int nvmlInit_v2();
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetCount_v2(out uint count);
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization u);
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);
    }
}
