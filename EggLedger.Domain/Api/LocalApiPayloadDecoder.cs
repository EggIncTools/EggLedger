using EggLedger.Domain.Ei;
using Ei;

namespace EggLedger.Domain.Api;

public sealed class LocalApiPayloadDecoder(ApiClient api) : IApiPayloadDecoder {
    public Task<EggIncFirstContactResponse> DecodeFirstContactAsync(byte[] rawPayload, CancellationToken ct = default) =>
        Task.FromResult(api.DecodeFirstContactPayload(rawPayload));

    public Task<CompleteMissionResponse> DecodeCompleteMissionAsync(byte[] rawPayload, CancellationToken ct = default) =>
        Task.FromResult(api.DecodeCompleteMissionPayload(rawPayload));
}
