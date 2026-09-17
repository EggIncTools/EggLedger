using System.Collections.Concurrent;

namespace EggLedger.Web.Server.Sync.Menno;

public sealed class IpRateLimiter(int maxPerWindow, TimeSpan window) {
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new();

    public bool Allow(string ip) {
        var now = DateTime.UtcNow;
        var cutoff = now - window;
        var q = _hits.GetOrAdd(ip, _ => new Queue<DateTime>());
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
