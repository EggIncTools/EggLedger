using EggLedger.Web.Data;

namespace EggLedger.Web.Server.Storage;

public sealed class UserDataOwner(CurrentUser user) : IDataOwner {
    public string? CachedKey { get; private set; }

    public async ValueTask<string> KeyAsync() {
        if (CachedKey is { } key) return key;
        var id = await user.RequireUserIdAsync().ConfigureAwait(false);
        return CachedKey = id.ToString("N");
    }
}
