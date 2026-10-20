using System.Text.Json;
using System.Text.Json.Serialization;
using EggIdentity.Contract;
using EggIdentity.UI.Suite;
using EggLedger.Domain.MissionQuery;
using Microsoft.Extensions.Logging;

namespace EggLedger.Web.Data;

public sealed class SuiteAccountStore(SuiteContext suite, IndexedDbSettings settings, ILogger<SuiteAccountStore>? logger = null) {
    internal const string LegacyAccountsKey = "known_accounts";
    internal const string LegacyActiveKey = "active_account_id";

    private Task? _ready;

    public IReadOnlyList<AccountInfo> Current => [.. suite.Eids.Select(ToAccount)];

    public async Task<List<AccountInfo>> GetKnownAccountsAsync() {
        await EnsureReadyAsync().ConfigureAwait(false);
        return [.. suite.Eids.Select(ToAccount)];
    }

    public Task EnsureReadyAsync() => _ready ??= LoadAsync();

    public async Task AddAccountAsync(AccountInfo account) {
        await EnsureReadyAsync().ConfigureAwait(false);
        var result = await suite.AddEidAsync(account.Id, null, ToSnapshot(account)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Log in to add an account.");
        if (result.Rejected is [var rejected, ..]) throw new InvalidOperationException(RejectionText(rejected.Reason));
    }

    public Task<bool> RefreshAccountAsync(AccountInfo account) =>
        suite.UpdateEidSnapshotAsync(account.Id, ToSnapshot(account));

    public Task<bool> RemoveAccountAsync(string id) => suite.RemoveEidAsync(id);

    public Task<EidUpsertResult?> ImportAsync(IEnumerable<AccountInfo> accounts) =>
        suite.UpsertEidsAsync([.. accounts.Select(a => new EidUpsert { Eid = a.Id, Snapshot = ToSnapshot(a) })]);

    public static AccountInfo ToAccount(ProfileEid entry) => new() {
        Id = entry.Eid,
        Nickname = entry.Label ?? entry.Snapshot?.GameName ?? "",
        EBString = entry.Snapshot?.EbText ?? "",
        AccountColor = entry.Snapshot?.Color is { Length: 6 } color && color.All(char.IsAsciiHexDigit) ? color : "",
        SeString = entry.Snapshot?.SeText ?? "",
        PeCount = entry.Snapshot?.PeCount ?? 0,
        TeCount = entry.Snapshot?.TeCount ?? 0,
        LastBackupTime = entry.Snapshot?.BackupAt ?? 0,
    };

    public static EidSnapshot ToSnapshot(AccountInfo account) => new() {
        GameName = NullIfEmpty(account.Nickname),
        Color = NullIfEmpty(account.AccountColor),
        EbText = NullIfEmpty(account.EBString),
        SeText = NullIfEmpty(account.SeString),
        PeCount = account.PeCount,
        TeCount = account.TeCount,
        BackupAt = account.LastBackupTime > 0 ? account.LastBackupTime : null,
    };

    public static string RejectionText(string reason) => reason switch {
        EidRules.FormatReason => "That is not a valid EID.",
        EidRules.LabelReason => "That label is too long.",
        EidRules.CapReason => $"You can save at most {EidRules.MaxEntries} accounts.",
        _ => "Could not add that account.",
    };

    private async Task LoadAsync() {
        try {
            await ImportLegacyAsync().ConfigureAwait(false);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) {
            logger?.LogWarning(ex, "legacy account import failed, retrying on next load");
            _ready = null;
        }
    }

    private async Task ImportLegacyAsync() {
        await suite.LoadAsync().ConfigureAwait(false);
        if (!suite.CanEditEids) return;
        var all = await settings.GetAllSettingsAsync().ConfigureAwait(false);
        if (!all.TryGetValue(LegacyAccountsKey, out var raw)) return;

        var legacy = ParseLegacy(raw);
        if (legacy.Count > 0) {
            var result = await ImportAsync(legacy).ConfigureAwait(false);
            if (result is null) return;
            foreach (var rejected in result.Rejected) {
                logger?.LogWarning("legacy account {Eid} not imported: {Reason}", EidRules.Mask(rejected.Eid), rejected.Reason);
            }
            if (suite.ActiveEid is null && all.TryGetValue(LegacyActiveKey, out var activeId)
                && suite.Eids.FirstOrDefault(e => e.Eid == activeId) is { } active) {
                await suite.SetActiveEidAsync(active.Eid, active.DisplayName).ConfigureAwait(false);
            }
        }
        await settings.RemoveSettingAsync(LegacyAccountsKey).ConfigureAwait(false);
        await settings.RemoveSettingAsync(LegacyActiveKey).ConfigureAwait(false);
    }

    private List<AccountInfo> ParseLegacy(string raw) {
        if (string.IsNullOrEmpty(raw)) return [];
        try {
            return JsonSerializer.Deserialize<List<LegacyAccountRow>>(raw, Rows.JsonOptions)?.ConvertAll(LegacyAccountRow.ToAccount) ?? [];
        } catch (JsonException ex) {
            logger?.LogDebug(ex, "legacy known accounts JSON unreadable, dropping it");
            return [];
        }
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}

public sealed record LegacyAccountRow {
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

    public static AccountInfo ToAccount(LegacyAccountRow r) => new() {
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
