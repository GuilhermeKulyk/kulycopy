using KulyCopy.Core;
using KulyCopy.Native;
using Microsoft.Win32;

namespace KulyCopy.Services;

/// <summary>
/// Iniciar com o Windows via HKCU\...\Run (sem privilégios de administrador).
/// Em instalação MSIX (Microsoft Store) o registro é virtualizado e não funciona; nesse caso o
/// usuário liga a tarefa de inicialização em Configurações › Aplicativos › Inicializar.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KulyCopy";

    /// <summary>True quando a inicialização é controlada pelo Windows (pacote MSIX), não por nós.</summary>
    public static bool IsManagedByWindows => PackageInfo.IsPackaged;

    public static bool IsEnabled()
    {
        if (IsManagedByWindows) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        if (IsManagedByWindows) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                key.SetValue(ValueName, "\"" + exe + "\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to update autostart registry entry", ex);
        }
    }
}
