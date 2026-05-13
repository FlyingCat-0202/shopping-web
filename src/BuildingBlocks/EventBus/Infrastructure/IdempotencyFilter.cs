using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EventBus.Infrastructure;

public class IdempotencyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        
        if (HttpMethods.IsGet(httpContext.Request.Method) || HttpMethods.IsHead(httpContext.Request.Method))
        {
            return await next(context);
        }

        if (!httpContext.Request.Headers.TryGetValue("x-requestid", out var requestIdValue)
            || !Guid.TryParse(requestIdValue, out var requestId))
        {
            return Results.BadRequest(new { message = "Header 'x-requestid' là bắt buộc và phải là một Guid hợp lệ cho các thao tác thay đổi dữ liệu." });
        }

        var idempotencyService = httpContext.RequestServices.GetRequiredService<IIdempotencyService>();

        if (await idempotencyService.RequestExistsAsync(requestId))
        {
            return Results.Conflict(new { message = "Yêu cầu này đã được xử lý trước đó." });
        }

        await idempotencyService.CreateRequestAsync(requestId);

        return await next(context);
    }
}
