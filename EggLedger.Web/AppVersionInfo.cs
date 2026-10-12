using System.Reflection;
using EggIdentity.Contract;

namespace EggLedger.Web;

public static class AppVersionInfo {
    public static string Informational =>
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    public static string Current {
        get {
            var info = Informational;
            var plus = info.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? info[..plus] : info;
        }
    }

    public static string Display => BuildVersion.Describe(Informational, null);

    public static string DescribeTag(string? tag) => BuildVersion.Describe(tag?.TrimStart('v'), null);
}
