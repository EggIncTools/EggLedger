using Microsoft.JSInterop;

namespace EggLedger.Web.Tests.Data;

public sealed class FakeJsRuntime(FakeJsObjectReference module) : IJSRuntime {
    public List<(string Identifier, object?[] Args)> Calls { get; } = [];

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) {
        Calls.Add((identifier, args ?? []));

        return identifier == "import"
            ? new ValueTask<TValue>((TValue)(object)module)
            : new ValueTask<TValue>(default(TValue)!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => InvokeAsync<TValue>(identifier, args);
}
