using Microsoft.Win32;

namespace BlackSunCyber.ClientAgent;

/// <summary>
/// Activează/dezactivează Task Manager-ul, ca să prevenim utilizatorii
/// să închidă manual procesul agentului prin Ctrl+Alt+Del -> Task Manager.
/// (Cod bazat pe schița din PDF-ul proiectului.)
/// </summary>
public static class SystemLockHelper
{
    public static void SetTaskManagerDisabled(bool disable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Policies\System");

            if (disable)
                key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
            else
                key.DeleteValue("DisableTaskMgr", false);
        }
        catch
        {
            // Dacă scrierea în Registry eșuează (ex: politici de grup care blochează),
            // nu oprim agentul din a porni — doar Task Manager-ul rămâne disponibil.
        }
    }
}