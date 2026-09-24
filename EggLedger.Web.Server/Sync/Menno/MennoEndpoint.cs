using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EggLedger.Web.Server.Sync.Menno;

public sealed record MennoRequest(
    [property: JsonPropertyName("eid")] string Eid);

public sealed partial class MennoEndpoint(HttpClient client, string functionKey, string upstreamUrl, ILogger<MennoEndpoint> logger) {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly IpRateLimiter Limiter = new(maxPerWindow: 10, window: TimeSpan.FromMinutes(1));
    [GeneratedRegex(@"^EI\d{16}$")]
    private static partial Regex EidPattern();

    public async Task Submit(HttpContext ctx) {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!Limiter.Allow(ip)) {
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        MennoRequest? body;
        try { body = await JsonSerializer.DeserializeAsync<MennoRequest>(ctx.Request.Body, Json, ctx.RequestAborted); } catch (JsonException ex) {
            logger.LogDebug(ex, "menno: malformed submit body");
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync("invalid request body: eid is required\n", ctx.RequestAborted);
            return;
        }
        if (body is null || string.IsNullOrEmpty(body.Eid) || !EidPattern().IsMatch(body.Eid)) {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync("invalid request body: eid must be EI followed by 16 digits\n", ctx.RequestAborted);
            return;
        }
        using var req = new HttpRequestMessage(HttpMethod.Post, upstreamUrl) {
            Content = JsonContent.Create(new { eid = body.Eid }),
        };
        req.Headers.Add("x-functions-key", functionKey);
        try {
            using var resp = await client.SendAsync(req, ctx.RequestAborted);
            ctx.Response.StatusCode = (int)resp.StatusCode;
        } catch (HttpRequestException ex) {
            logger.LogDebug(ex, "menno: upstream submit failed");
            ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }
}
