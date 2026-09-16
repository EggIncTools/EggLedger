using System.Security.Cryptography;
using System.Text.Json;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Data;
using EggLedger.Web.Services;
using EggLedger.Web.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EggLedger.Web.Server.Storage;

public sealed class FirstLoginBackfill(
    NpgsqlDataSource source,
    CurrentUser user,
    IBlobCipher cipher,
    IndexedDbAccountStore accounts,
    IndexedDbSettings settings,
    IndexedDbReportStore reports,
    IDataProtectionProvider dataProtection,
    ILogger<FirstLoginBackfill> logger) {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _keyProtector = dataProtection.CreateProtector("EggLedger.EncryptionKey");

    public async Task RunIfNeededAsync(CancellationToken ct = default) {
        var userId = await user.GetUserIdAsync().ConfigureAwait(false);
        if (userId is null) {
            return;
        }

        var known = await accounts.GetKnownAccountsAsync().ConfigureAwait(false);
        if (known.Count > 0) {
            return;
        }

        var encKey = await EncryptionKeyAsync(userId.Value, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(encKey)) {
            return;
        }

        await RestoreAccountsAsync(userId.Value, encKey, ct).ConfigureAwait(false);
        await RestoreSettingsAsync(userId.Value, encKey, ct).ConfigureAwait(false);
        await RestoreReportsAsync(userId.Value, encKey, ct).ConfigureAwait(false);
    }

    private async Task RestoreAccountsAsync(Guid userId, string encKey, CancellationToken ct) {
        var list = await DecryptBlobAsync<List<AccountInfo>>(userId, encKey, CloudSyncBlobs.AccountsBlob, ct)
            .ConfigureAwait(false);
        if (list is null) {
            return;
        }
        foreach (var acct in list) {
            await accounts.AddKnownAccountAsync(acct).ConfigureAwait(false);
        }
    }

    private async Task RestoreSettingsAsync(Guid userId, string encKey, CancellationToken ct) {
        var blob = await DecryptBlobAsync<CloudSyncableSettings>(userId, encKey, CloudSyncBlobs.SettingsBlob, ct)
            .ConfigureAwait(false);
        if (blob is not null) {
            await settings.SetSettingsAsync(CloudSyncBlobs.UnpackSettings(blob)).ConfigureAwait(false);
        }
    }

    private async Task RestoreReportsAsync(Guid userId, string encKey, CancellationToken ct) {
        var blob = await DecryptBlobAsync<CloudReportsBlob>(userId, encKey, CloudSyncBlobs.ReportsBlob, ct)
            .ConfigureAwait(false);
        if (blob is null) {
            return;
        }
        var existingGroups = (await reports.RetrieveAccountGroupsAsync("").ConfigureAwait(false)).Select(g => g.Id).ToList();
        var existingReports = new List<string>();
        var (groups, rows) = CloudSyncBlobs.SelectReportsToImport(blob, existingGroups, existingReports);
        foreach (var g in groups) {
            await reports.InsertReportGroupAsync(g).ConfigureAwait(false);
        }
        foreach (var r in rows) {
            await reports.InsertReportAsync(r).ConfigureAwait(false);
        }
    }



    private async Task<string?> EncryptionKeyAsync(Guid userId, CancellationToken ct) {
        await using var cmd = source.CreateCommand("SELECT encryption_key FROM users WHERE user_id = $1");
        cmd.Parameters.AddWithValue(userId);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string { Length: > 0 } stored) {
            return null;
        }

        try {
            return _keyProtector.Unprotect(stored);
        } catch (CryptographicException) {
            return stored;
        }
    }

    private async Task<T?> DecryptBlobAsync<T>(Guid userId, string encKey, string name, CancellationToken ct) {
        string? ciphertext = null;
        await using (var cmd = source.CreateCommand("SELECT ciphertext FROM blobs WHERE user_id = $1 AND name = $2")) {
            cmd.Parameters.AddWithValue(userId);
            cmd.Parameters.AddWithValue(name);
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            ciphertext = result as string;
        }
        if (string.IsNullOrEmpty(ciphertext)) {
            return default;
        }
        try {
            var plaintext = await cipher.DecryptAsync(encKey, ciphertext, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(plaintext, Json);
        } catch (Exception ex) {


            logger.LogWarning(ex, "backfill: failed to decrypt blob {Name}", name);
            return default;
        }
    }
}
