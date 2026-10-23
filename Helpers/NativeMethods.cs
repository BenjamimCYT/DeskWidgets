using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskWidgets.Services;

namespace DeskWidgets.Helpers
{
    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOOLWINDOW = 0x00000080;
        public const long WS_EX_APPWINDOW = 0x00040000;

        public const int WM_WINDOWPOSCHANGING = 0x0046;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;

        public const int ProcessPowerThrottling = 4;
        public const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass,
            ref PROCESS_POWER_THROTTLING_STATE processInformation, uint processInformationSize);

        [DllImport("psapi.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);
    }

    internal static class ResourceSaver
    {
        /// <summary>
        /// Prioridade baixa + EcoQoS = "Modo de eficiência" do Windows 11 (folhinha verde no Gerenciador de Tarefas).
        /// O widget nunca disputa CPU com jogos ou outros programas.
        /// </summary>
        public static void Apply()
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle; }
            catch (Exception ex) { SettingsService.Log(ex); }

            try
            {
                var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
                {
                    Version = 1,
                    ControlMask = NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                    StateMask = NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED
                };
                NativeMethods.SetProcessInformation(NativeMethods.GetCurrentProcess(), NativeMethods.ProcessPowerThrottling,
                    ref state, (uint)Marshal.SizeOf<NativeMethods.PROCESS_POWER_THROTTLING_STATE>());
            }
            catch (Exception ex) { SettingsService.Log(ex); }
        }

        /// <summary>Devolve ao Windows a memória que o app não está usando (após carregar e após cada atualização).</summary>
        public static void Trim()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Optimized, false);
                NativeMethods.EmptyWorkingSet(NativeMethods.GetCurrentProcess());
            }
            catch { }
        }
    }
}
