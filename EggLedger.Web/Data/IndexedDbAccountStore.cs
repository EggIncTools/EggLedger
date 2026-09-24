using System.Text.Json;
using System.Text.Json.Serialization;
using EggLedger.Domain.MissionQuery;
using Microsoft.Extensions.Logging;

namespace EggLedger.Web.Data;

public sealed class IndexedDbAccountStore(IndexedDbSettings settings, ILogger<IndexedDbAccountStore>? logger = null) {
    internal const string KnownAccountsKey = "known_accounts";
    internal const string ActiveAccountKey = "active_account_id";

    public async Task<List<AccountInfo>> GetKnownAccountsAsync() {
        var all = await settings.GetAllSettingsAsync();
        return Deserialize(all);
    }

    public async Task AddKnownAccountAsync(AccountInfo account) {
        var all = await settings.GetAllSettingsAsync();
        var list = Deserialize(all);
        int i = list.FindIndex(a => a.Id == account.Id);
        if (i >= 0) {
            list[i] = account;
        } else {
            list.Add(account);
        }
        await settings.SetSettingAsync(KnownAccountsKey, Serialize(list));
    }

    public async Task RemoveKnownAccountAsync(string id) {
        var all = await settings.GetAllSettingsAsync();
        var list = Deserialize(all);
        int removed = list.RemoveAll(a => a.Id == id);
        if (removed > 0) {
            await settings.SetSettingAsync(KnownAccountsKey, Serialize(list));
        }
    }

    public async Task<string?> GetActiveAccountIdAsync() {
        var all = await settings.GetAllSettingsAsync();
        return all.TryGetValue(ActiveAccountKey, out var id) && !string.IsNullOrEmpty(id) ? id : null;
    }

    public async Task SetActiveAccountIdAsync(string id) =>
        await settings.SetSettingAsync(ActiveAccountKey, id ?? "");

    private List<AccountInfo> Deserialize(Dictionary<string, string> all) {
        if (!all.TryGetValue(KnownAccountsKey, out var raw) || string.IsNullOrEmpty(raw)) {
            return [];
        }
        try {
            var rows = JsonSerializer.Deserialize<List<AccountInfoRow>>(raw, Rows.JsonOptions);
            return rows is null
                ? []
                : rows.ConvertAll(AccountInfoRow.ToAccount);
        } catch (JsonException ex) {
            logger?.LogDebug(ex, "known accounts JSON unreadable, treating as empty");
            return [];
        }
    }

    private static string Serialize(List<AccountInfo> list) {
        var rows = list.ConvertAll(AccountInfoRow.FromAccount);
        return JsonSerializer.Serialize(rows, Rows.JsonOptions);
    }
}

public sealed record AccountInfoRow {
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("nickname")]
    public string Nickname { get; init; } = "";
    [JsonPropertyName("eb_string")]
    public string EBString { get; init; } = "";
    [JsonPropertyName("account_color")]
    public string AccountColor { get; init; } = "";
    [JsonPropertyName("se_string")]
    public string SeString { get; init; } = "";
    [JsonPropertyName("pe_count")]
    public int PeCount { get; init; }
    [JsonPropertyName("te_count")]
    public int TeCount { get; init; }
    [JsonPropertyName("last_backup_time")]
    public double LastBackupTime { get; init; }

    public static AccountInfoRow FromAccount(AccountInfo a) => new() {
        Id = a.Id,
        Nickname = a.Nickname,
        EBString = a.EBString,
        AccountColor = a.AccountColor,
        SeString = a.SeString,
        PeCount = a.PeCount,
        TeCount = a.TeCount,
        LastBackupTime = a.LastBackupTime,
    };

    public static AccountInfo ToAccount(AccountInfoRow r) => new() {
        Id = r.Id,
        Nickname = r.Nickname,
        EBString = r.EBString,
        AccountColor = r.AccountColor,
        SeString = r.SeString,
        PeCount = r.PeCount,
        TeCount = r.TeCount,
        LastBackupTime = r.LastBackupTime,
    };
}
