using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace DeskWidgets.Services
{
    /// <summary>
    /// Cria/atualiza "DeskWidgets" no menu Iniciar do usuário
    /// (%AppData%\Microsoft\Windows\Start Menu\Programs). Não precisa de administrador.
    /// </summary>
    public static class ShortcutService
    {
        private static string LinkPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DeskWidgets.lnk");

        public static bool Exists() => File.Exists(LinkPath);

        /// <summary>Cria ou remove o atalho. Recria sempre para acompanhar o .exe se ele mudar de pasta.</summary>
        public static bool Set(bool enable)
        {
            try
            {
                if (!enable)
                {
                    if (File.Exists(LinkPath)) File.Delete(LinkPath);
                    return true;
                }

                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;

                var link = (IShellLinkW)new ShellLink();
                link.SetPath(exe);
                link.SetWorkingDirectory(Path.GetDirectoryName(exe));
                link.SetDescription("Widgets de área de trabalho: relógio, calendário, clima, bateria e notas");
                link.SetIconLocation(exe, 0);
                ((IPersistFile)link).Save(LinkPath, true);
                Marshal.FinalReleaseComObject(link);
                return true;
            }
            catch (Exception ex)
            {
                SettingsService.Log(ex);
                return false;
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
