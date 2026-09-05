using EggIdentity.Resilience;
using Microsoft.Extensions.Logging;

namespace EggLedger.Web.Data;

public sealed class ResilientIndexedDb : IIndexedDb {
    private readonly IIndexedDb _inner;
    private readonly ILogger<ResilientIndexedDb> _logger;
    private readonly RetryOptions _retry;

    public ResilientIndexedDb(IIndexedDb inner, ILogger<ResilientIndexedDb> logger, Func<Exception, bool>? isTransient = null) {
        _inner = inner;
        _logger = logger;
        _retry = new RetryOptions {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(100),
            ShouldRetry = ex => ex is IOException or TimeoutException || isTransient?.Invoke(ex) == true,
        };
    }

    private async Task<T> RunAsync<T>(string op, string store, Func<Task<T>> body) {
        try {
            return await Retry.RunAsync(_ => body(), _retry).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger.LogError(ex, "IIndexedDb.{Op} failed for store {Store} after retries", op, store);
            throw;
        }
    }

    private async Task RunAsync(string op, string store, Func<Task> body) {
        await RunAsync(op, store, async () => {
            await body().ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    public async ValueTask PutAsync(string store, object value) =>
        await RunAsync(nameof(PutAsync), store, () => _inner.PutAsync(store, value).AsTask()).ConfigureAwait(false);

    public async ValueTask<int> PutManyAsync(string store, IEnumerable<object> values) =>
        await RunAsync(nameof(PutManyAsync), store, () => _inner.PutManyAsync(store, values).AsTask()).ConfigureAwait(false);

    public async ValueTask<T?> GetAsync<T>(string store, object key) =>
        await RunAsync(nameof(GetAsync), store, () => _inner.GetAsync<T>(store, key).AsTask()).ConfigureAwait(false);

    public async ValueTask<T[]> GetAllAsync<T>(string store) =>
        await RunAsync(nameof(GetAllAsync), store, () => _inner.GetAllAsync<T>(store).AsTask()).ConfigureAwait(false);

    public async ValueTask<T[]> GetAllByIndexAsync<T>(string store, string index, object value) =>
        await RunAsync(nameof(GetAllByIndexAsync), store, () => _inner.GetAllByIndexAsync<T>(store, index, value).AsTask()).ConfigureAwait(false);

    public async ValueTask<T[]> GetAllByIndexProjectedAsync<T>(string store, string index, object value) =>
        await RunAsync(nameof(GetAllByIndexProjectedAsync), store, () => _inner.GetAllByIndexProjectedAsync<T>(store, index, value).AsTask()).ConfigureAwait(false);

    public async ValueTask DeleteAsync(string store, object key) =>
        await RunAsync(nameof(DeleteAsync), store, () => _inner.DeleteAsync(store, key).AsTask()).ConfigureAwait(false);

    public async ValueTask ClearAsync(string store) =>
        await RunAsync(nameof(ClearAsync), store, () => _inner.ClearAsync(store).AsTask()).ConfigureAwait(false);

    public async ValueTask<int> CountAsync(string store) =>
        await RunAsync(nameof(CountAsync), store, () => _inner.CountAsync(store).AsTask()).ConfigureAwait(false);
}
