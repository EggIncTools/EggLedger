namespace EggLedger.Web.Server.Sync.Db;

public static class SessionTokens {
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);
}
