using System.Text.Json;
using System.Text.Json.Serialization;
using EggLedger.Domain.MissionQuery;

namespace EggLedger.Web.Data;

public sealed class IndexedDbAccountStore {
    internal const string KnownAccountsKey = "known_accounts";
    internal const string ActiveAccountKey = "active_account_id";
    private readonly IndexedDbSettings _settings;

    public IndexedDbAccountStore(IndexedDbSettings settings) {
        _settings = settings;
    }

    public async Task<List<AccountInfo>> GetKnownAccountsAsync() {
        var all = await _settings.GetAllSettingsAsync();
        return Deserialize(all);
    }

    public async Task AddKnownAccountAsync(AccountInfo account) {
        var all = await _settings.GetAllSettingsAsync();
        var list = Deserialize(all);
        int i = list.FindIndex(a => a.Id == account.Id);
        if (i >= 0) {
            list[i] = account;
        } else {
            list.Add(account);
        }
        await _settings.SetSettingAsync(KnownAccountsKey, Serialize(list));
    }

    public async Task RemoveKnownAccountAsync(string id) {
        var all = await _settings.GetAllSettingsAsync();
        var list = Deserialize(all);
        int removed = list.RemoveAll(a => a.Id == id);
        if (removed > 0) {
            await _settings.SetSettingAsync(KnownAccountsKey, Serialize(list));
        }
    }

    public async Task<string?> GetActiveAccountIdAsync() {
        var all = await _settings.GetAllSettingsAsync();
        if (all.TryGetValue(ActiveAccountKey, out var id) && !string.IsNullOrEmpty(id)) {
            return id;
        }
        return null;
    }

    public async Task SetActiveAccountIdAsync(string id) =>
        await _settings.SetSettingAsync(ActiveAccountKey, id ?? "");

    private static List<AccountInfo> Deserialize(Dictionary<string, string> settings) {
        if (!settings.TryGetValue(KnownAccountsKey, out var raw) || string.IsNullOrEmpty(raw)) {
            return [];
        }
        try {
            var rows = JsonSerializer.Deserialize<List<AccountInfoRow>>(raw, Rows.JsonOptions);
            return rows is null
                ? []
                : rows.ConvertAll(AccountInfoRow.ToAccount);
        } catch (JsonException) {

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
