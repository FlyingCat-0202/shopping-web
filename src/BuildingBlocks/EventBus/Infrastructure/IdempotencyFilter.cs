using Microsoft.AspNetCore.Http;
using StackExchange.Redis;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EventBus.Infrastructure;

public record CachedResponse(int StatusCode, object? Value);

public class IdempotencyFilter(IConnectionMultiplexer redis) : IEndpointFilter
{
    private const string ProcessingValue = "IN_PROGRESS";
    private static readonly TimeSpan ProcessingTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ResponseTtl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var requestId = httpContext.Request.Headers["x-requestid"].FirstOrDefault()?.Trim();

        if (string.IsNullOrEmpty(requestId))
        {
            return Results.BadRequest(new { message = "Thiếu x-requestid" });
        }

        var redisKey = BuildRedisKey(context, requestId);
        var db = redis.GetDatabase();

        var isNew = await db.StringSetAsync(redisKey, ProcessingValue, ProcessingTtl, When.NotExists);

        if (!isNew)
        {
            var cachedJson = await db.StringGetAsync(redisKey);
            if (cachedJson == ProcessingValue)
                return Results.Conflict(new { message = "Yêu cầu đang xử lý." });

            var jsonString = (string)cachedJson!;

            var oldResponse = JsonSerializer.Deserialize<CachedResponse>(jsonString, JsonOptions);

            if (oldResponse is null)
            {
                await db.KeyDeleteAsync(redisKey);
                return Results.BadRequest(new { message = "Dữ liệu phản hồi cũ trong hệ thống không hợp lệ. Vui lòng thử lại." });
            }

            return Results.Json(oldResponse.Value, statusCode: oldResponse.StatusCode);
        }

        try
        {
            var result = await next(context);

            int statusCode = 200;
            object? responseValue = null;

            if (result is IValueHttpResult valueResult)
            {
                responseValue = valueResult.Value;
            }

            if (result is IStatusCodeHttpResult statusResult)
            {
                statusCode = statusResult.StatusCode ?? 200;
            }

            if (statusCode >= StatusCodes.Status500InternalServerError)
            {
                await db.KeyDeleteAsync(redisKey);
                return result;
            }

            var responseToCache = new CachedResponse(statusCode, responseValue);
            var jsonToSave = JsonSerializer.Serialize(responseToCache, JsonOptions);
            await db.StringSetAsync(redisKey, jsonToSave, ResponseTtl);

            return result;
        }
        catch
        {
            await db.KeyDeleteAsync(redisKey);
            throw;
        }
    }

    private static string BuildRedisKey(EndpointFilterInvocationContext context, string requestId)
    {
        var request = context.HttpContext.Request;
        var userScope = GetUserScope(context.HttpContext.User);
        var argumentHash = HashEndpointArguments(context.Arguments);
        var pathAndQueryHash = ComputeSha256($"{request.Path}{request.QueryString}");

        return $"idempotency:{request.Method}:{userScope}:{requestId}:{pathAndQueryHash}:{argumentHash}";
    }

    private static string GetUserScope(ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? user.FindFirst("sub")?.Value
           ?? "anonymous";

    private static string HashEndpointArguments(IEnumerable<object?> arguments)
    {
        var inputArguments = arguments
            .Where(ShouldHashArgument)
            .Select(argument => argument!)
            .ToList();

        if (inputArguments.Count == 0)
            return "no-body";

        return ComputeSha256(JsonSerializer.Serialize(inputArguments, JsonOptions));
    }

    private static bool ShouldHashArgument(object? argument)
    {
        if (argument is null)
            return false;

        if (argument is HttpContext or HttpRequest or HttpResponse or ClaimsPrincipal or CancellationToken)
            return false;

        var type = argument.GetType();
        var namespaceName = type.Namespace ?? string.Empty;

        return !namespaceName.StartsWith("Microsoft.", StringComparison.Ordinal) &&
               !namespaceName.StartsWith("MassTransit", StringComparison.Ordinal) &&
               !namespaceName.StartsWith("StackExchange.Redis", StringComparison.Ordinal) &&
               (type.GetProperties().Length > 0 || type.GetFields().Length > 0) &&
               !type.Name.EndsWith("DbContext", StringComparison.Ordinal) &&
               !type.Name.Contains("Logger", StringComparison.Ordinal);
    }

    private static string ComputeSha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
