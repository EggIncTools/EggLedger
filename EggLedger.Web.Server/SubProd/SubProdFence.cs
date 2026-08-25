namespace EggLedger.Web.Server.SubProd;

public static class SubProdFence {
    public const string SubProdDatabaseName = "eggledger_subprod";
    public const string RequiredEnvironment = "Staging";
    private const string IdentityLoopbackPlaceholder = "http://127.0.0.1:1";
    private const string SessionSecretPlaceholder = "eggledger-subprod-local-only-not-a-real-secret";

    public sealed record FenceEntry(string Key, bool Forced);

    private static readonly (string AllowName, string[] Keys)[] Gates = [
        ("DISCORD", ["DISCORD_BOT_TOKEN", "DISCORD_CLIENT_ID"]),
        ("DEPLOY", ["DEPLOY_AGENT_URL", "DEPLOY_AGENT_SECRET"]),
        ("AUTH", ["AUTHENTIK_CLIENT_SECRET", "IDENTITY_API_SECRET"]),
    ];

    public static bool IsStaging(string environmentName) =>
        string.Equals(environmentName, RequiredEnvironment, StringComparison.OrdinalIgnoreCase);

    public static bool Allows(string allowName, Func<string, string?> get) =>
        string.Equals(get($"SUBPROD_ALLOW_{allowName}"), "true", StringComparison.OrdinalIgnoreCase);

    public static Func<string, string?> WrapGetter(Func<string, string?> inner, string environmentName, out IReadOnlyList<FenceEntry> report) {
        var entries = new List<FenceEntry>();
        report = entries;

        if (!IsStaging(environmentName)) {
            return inner;
        }

        var blanked = new HashSet<string>();
        foreach (var (allowName, keys) in Gates) {
            var allowed = Allows(allowName, inner);
            foreach (var key in keys) {
                entries.Add(new FenceEntry(key, Forced: !allowed));
                if (!allowed) {
                    blanked.Add(key);
                }
            }
        }

        var authAllowed = Allows("AUTH", inner);
        entries.Add(new FenceEntry("IDENTITY_API_URL", Forced: !authAllowed));

        return key => {
            if (key == "IDENTITY_API_URL" && !authAllowed) {
                return IdentityLoopbackPlaceholder;
            }
            return blanked.Contains(key) ? "" : inner(key);
        };
    }

    public static void ForceSessionIsolation(Action<string, string?> setEnvironmentVariable, string environmentName) {
        if (!IsStaging(environmentName)) {
            return;
        }
        setEnvironmentVariable("EGGIDENTITY_SESSION_SECRET", SessionSecretPlaceholder);
        setEnvironmentVariable("EGGIDENTITY_SESSION_SECRET_PREVIOUS", SessionSecretPlaceholder);
        setEnvironmentVariable("EGGIDENTITY_SESSION_COOKIE_DOMAIN", null);
    }
}
