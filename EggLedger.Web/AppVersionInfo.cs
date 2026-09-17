using System.Reflection;

namespace EggLedger.Web;

public static class AppVersionInfo {
    public static string Current {
        get {
            var info = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(info)) {
                return "";
            }
            var plus = info.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? info[..plus] : info;
        }
    }
}
