using Ei;

namespace EggLedger.Domain.Api;

public interface IApiPayloadDecoder {
    Task<EggIncFirstContactResponse> DecodeFirstContactAsync(byte[] rawPayload, CancellationToken ct = default);
    Task<CompleteMissionResponse> DecodeCompleteMissionAsync(byte[] rawPayload, CancellationToken ct = default);
}
