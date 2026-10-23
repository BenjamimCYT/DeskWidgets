using System;
using Microsoft.Win32;

namespace DeskWidgets.Services
{
    /// <summary>Usa HKCU\...\Run (não precisa de administrador).</summary>
    public static class StartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DeskWidgets";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(ValueName) is string;
            }
            catch { return false; }
        }

        public static bool Set(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (enable) key.SetValue(ValueName, "\"" + Environment.ProcessPath + "\"");
                else key.DeleteValue(ValueName, false);
                return true;
            }
            catch (Exception ex)
            {
                SettingsService.Log(ex);
                return false;
            }
        }
    }
}
