using Microsoft.JSInterop;

namespace EggLedger.Web.Tests.Data;

public sealed class FakeJsObjectReference : IJSObjectReference {
    public List<(string Identifier, object?[] Args)> Calls { get; } = [];
    private readonly Dictionary<string, Queue<object?>> _canned = [];
    public bool Disposed { get; private set; }

    public void Enqueue(string identifier, object? value) {
        if (!_canned.TryGetValue(identifier, out var queue)) {
            queue = new Queue<object?>();
            _canned[identifier] = queue;
        }

        queue.Enqueue(value);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) {
        Calls.Add((identifier, args ?? []));

        return _canned.TryGetValue(identifier, out var queue) && queue.Count > 0
            ? new ValueTask<TValue>((TValue)queue.Dequeue()!)
            : new ValueTask<TValue>(default(TValue)!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => InvokeAsync<TValue>(identifier, args);

    public ValueTask DisposeAsync() {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
