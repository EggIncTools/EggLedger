using EggLedger.Domain.Api;
using EggLedger.Domain.Ei;
using EggLedger.Domain.MissionQuery;
using EggLedger.Web.Data;

namespace EggLedger.Web.Services;

public sealed class AddAccountService(ApiClient api, SuiteAccountStore accounts, IApiPayloadDecoder decoder) {
    private static readonly TimeSpan FirstContactTimeout = TimeSpan.FromSeconds(20);

    public async Task<AccountInfo> AddAccountAsync(string eid, CancellationToken cancellationToken = default) {
        var account = await FirstContactAsync(eid, cancellationToken).ConfigureAwait(false);
        await accounts.AddAccountAsync(account).ConfigureAwait(false);
        return account;
    }

    public async Task<AccountInfo> FirstContactAsync(string eid, CancellationToken cancellationToken = default) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(FirstContactTimeout);

        byte[] payload = await api.RequestFirstContactRawPayloadAsync(eid, cts.Token).ConfigureAwait(false);
        var fc = await decoder.DecodeFirstContactAsync(payload, cts.Token).ConfigureAwait(false);
        var invalid = fc.Validate();
        if (invalid is not null) {
            throw new InvalidOperationException(
                $"please double check your ID: error fetching backup for player {eid}: {invalid.Message}", invalid);
        }

        return AccountFactory.FromBackup(eid, fc.Backup!);
    }
}
