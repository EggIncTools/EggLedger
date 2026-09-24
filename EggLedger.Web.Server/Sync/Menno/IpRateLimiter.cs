using System.Collections.Concurrent;

namespace EggLedger.Web.Server.Sync.Menno;

public sealed class IpRateLimiter(int maxPerWindow, TimeSpan window, TimeProvider? time = null) {
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _hits = new();

    public bool Allow(string ip) {
        var now = _time.GetUtcNow();
        var cutoff = now - window;
        var q = _hits.GetOrAdd(ip, _ => new Queue<DateTimeOffset>());
        lock (q) {
            while (q.Count > 0 && q.Peek() < cutoff) {
                q.Dequeue();
            }
            if (q.Count >= maxPerWindow) {
                return false;
            }
            q.Enqueue(now);
            return true;
        }
    }
}
