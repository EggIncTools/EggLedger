using System.Text.Json;
using System.Text.Json.Serialization;

namespace EggLedger.Domain.Api;

public static class ApiPayloadJson {
    public static JsonSerializerOptions Options { get; } = new() {
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
    };
}
