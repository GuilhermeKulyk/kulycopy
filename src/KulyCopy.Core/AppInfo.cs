using System.Reflection;

namespace KulyCopy.Core;

public static class AppInfo
{
    public const string Name = "KulyCopy";
    public const string License = "MIT";

    public static string Version
    {
        get
        {
            var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
            {
                // Remove o sufixo "+hash" que o SDK adiciona.
                var plus = info.IndexOf('+');
                return plus > 0 ? info[..plus] : info;
            }
            return asm.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }
}
