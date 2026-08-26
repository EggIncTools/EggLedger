using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EggLedger.Web.Server.Sync.Blobs;



public sealed class BlobEndpoints(NpgsqlDataSource source, ILogger<BlobEndpoints> logger) {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();



    private static Guid UserId(HttpContext ctx) => Guid.Parse(ctx.Request.Headers["X-Discord-ID"].ToString());

    private static async Task WriteTextAsync(HttpContext ctx, int statusCode, string text) {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(text), ctx.RequestAborted);
    }

    private static async Task WriteJsonAsync<T>(HttpContext ctx, T value) {
        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, value, Json, ctx.RequestAborted);
    }

    public async Task Put(HttpContext ctx, string name) {
        var userId = UserId(ctx);
        PutBlobRequest? body;
        try { body = await JsonSerializer.DeserializeAsync<PutBlobRequest>(ctx.Request.Body, Json, ctx.RequestAborted); } catch (JsonException) {
            await WriteTextAsync(ctx, StatusCodes.Status400BadRequest, "bad request\n");
            return;
        }
        if (body is null) {
            await WriteTextAsync(ctx, StatusCodes.Status400BadRequest, "bad request\n");
            return;
        }



        try {
            await using var cmd = source.CreateCommand(
                "INSERT INTO blobs (user_id, name, ciphertext, updated_at) VALUES ($1, $2, $3, $4) " +
                "ON CONFLICT (user_id, name) DO UPDATE SET ciphertext = EXCLUDED.ciphertext, updated_at = EXCLUDED.updated_at");
            cmd.Parameters.AddWithValue(userId);
            cmd.Parameters.AddWithValue(name);
            cmd.Parameters.AddWithValue(body.Ciphertext);
            cmd.Parameters.AddWithValue(Now());
            await cmd.ExecuteNonQueryAsync(ctx.RequestAborted);
        } catch (Exception ex) {
            logger.LogWarning(ex, "blobs: failed to put blob {Name} for {UserId}", name, userId);
            await WriteTextAsync(ctx, StatusCodes.Status500InternalServerError, "internal error\n");
            return;
        }
        ctx.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    public async Task Get(HttpContext ctx, string name) {
        var userId = UserId(ctx);
        try {
            await using var cmd = source.CreateCommand("SELECT ciphertext, updated_at FROM blobs WHERE user_id = $1 AND name = $2");
            cmd.Parameters.AddWithValue(userId);
            cmd.Parameters.AddWithValue(name);
            await using var reader = await cmd.ExecuteReaderAsync(ctx.RequestAborted);
            if (!await reader.ReadAsync(ctx.RequestAborted)) {
                await WriteTextAsync(ctx, StatusCodes.Status404NotFound, "not found\n");
                return;
            }
            var resp = new GetBlobResponse(reader.GetString(0), reader.GetInt64(1));
            await WriteJsonAsync(ctx, resp);
        } catch (Exception ex) {
            logger.LogWarning(ex, "blobs: failed to get blob {Name} for {UserId}", name, userId);
            await WriteTextAsync(ctx, StatusCodes.Status500InternalServerError, "internal error\n");
        }
    }

    public async Task List(HttpContext ctx) {
        var userId = UserId(ctx);
        try {
            await using var cmd = source.CreateCommand("SELECT name, updated_at FROM blobs WHERE user_id = $1");
            cmd.Parameters.AddWithValue(userId);
            var items = new List<BlobListEntry>();
            await using var reader = await cmd.ExecuteReaderAsync(ctx.RequestAborted);
            while (await reader.ReadAsync(ctx.RequestAborted))
                items.Add(new BlobListEntry(reader.GetString(0), reader.GetInt64(1)));
            await WriteJsonAsync(ctx, items);
        } catch (Exception ex) {
            logger.LogWarning(ex, "blobs: failed to list blobs for {UserId}", userId);
            await WriteTextAsync(ctx, StatusCodes.Status500InternalServerError, "internal error\n");
        }
    }

    public async Task Delete(HttpContext ctx, string name) {
        var userId = UserId(ctx);
        await using var cmd = source.CreateCommand("DELETE FROM blobs WHERE user_id = $1 AND name = $2");
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(name);
        try {
            await cmd.ExecuteNonQueryAsync(ctx.RequestAborted);
        } catch (Exception ex) {
            logger.LogWarning(ex, "blobs: failed to delete blob {Name} for {UserId}", name, userId);
            await WriteTextAsync(ctx, StatusCodes.Status500InternalServerError, "internal error\n");
            return;
        }
        ctx.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    public async Task DeleteUser(HttpContext ctx) {
        var userId = UserId(ctx);
        try {
            await UserDataDeletion.DeleteUserAsync(source, userId, ctx.RequestAborted);
        } catch (Exception ex) {
            logger.LogWarning(ex, "blobs: failed to delete user {UserId}", userId);
            await WriteTextAsync(ctx, StatusCodes.Status500InternalServerError, "internal error\n");
            return;
        }
        ctx.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}
