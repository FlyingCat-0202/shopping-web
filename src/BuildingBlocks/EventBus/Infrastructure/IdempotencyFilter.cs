using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System.Text.Json;

namespace EventBus.Infrastructure;

public record CachedResponse(int StatusCode, object? Value);

public class IdempotencyFilter : IEndpointFilter
{
    private readonly IConnectionMultiplexer _redis;

    public IdempotencyFilter(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var requestId = httpContext.Request.Headers["x-requestid"].FirstOrDefault();

        if (string.IsNullOrEmpty(requestId))
        {
            return Results.BadRequest(new { message = "Thiếu x-requestid" });
        }

        var redisKey = $"idempotency:{httpContext.Request.Path}:{requestId}";
        var db = _redis.GetDatabase(); // Tự inject Redis Multiplexer vào class nhé

        // KHÓA REQUEST
        bool isNew = await db.StringSetAsync(redisKey, "IN_PROGRESS", TimeSpan.FromHours(24), When.NotExists);

        if (!isNew)
        {
            var cachedJson = await db.StringGetAsync(redisKey);
            if (cachedJson == "IN_PROGRESS")
                return Results.Conflict(new { message = "Yêu cầu đang xử lý." });

            var jsonString = (string)cachedJson!;

            CachedResponse? oldResponse = JsonSerializer.Deserialize<CachedResponse>(jsonString);

            // 3. KIỂM TRA NULL: Nếu cache bị lỗi/corrupted, xóa key lỗi và báo cho client biết
            if (oldResponse is null)
            {
                await db.KeyDeleteAsync(redisKey); // Xóa key lỗi để lần sau client có thể thử lại
                return Results.BadRequest(new { message = "Dữ liệu phản hồi cũ trong hệ thống không hợp lệ. Vui lòng thử lại." });
            }

            // 4. Trả về kết quả cũ an toàn
            return Results.Json(oldResponse.Value, statusCode: oldResponse.StatusCode);
        }

        try
        {
            // THỰC THI ENDPOINT
            var result = await next(context);

            // BÓC TÁCH IRESULT CHUẨN XÁC
            int statusCode = 200;
            object? responseValue = null;

            // Lấy value (data JSON)
            if (result is IValueHttpResult valueResult)
            {
                responseValue = valueResult.Value;
            }

            // Lấy status code (200, 201, 202, 400...)
            if (result is IStatusCodeHttpResult statusResult)
            {
                statusCode = statusResult.StatusCode ?? 200;
            }

            // LƯU VÀO REDIS
            var responseToCache = new CachedResponse(statusCode, responseValue);
            var jsonToSave = JsonSerializer.Serialize(responseToCache);
            await db.StringSetAsync(redisKey, jsonToSave, TimeSpan.FromHours(24));

            return result;
        }
        catch
        {
            // Bắt buộc xóa nếu lỗi
            await db.KeyDeleteAsync(redisKey);
            throw;
        }
    }
}
