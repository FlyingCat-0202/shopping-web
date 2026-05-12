using Order.Infrastructure.Data;

namespace Order.API.Extensions;

public class IdempotencyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (HttpMethods.IsGet(httpContext.Request.Method) || HttpMethods.IsHead(httpContext.Request.Method))
            return await next(context);

        if (!httpContext.Request.Headers.TryGetValue("x-requestid", out var requestIdValue)
            || !Guid.TryParse(requestIdValue, out var requestId))
        {
            return Results.BadRequest(new { message = "Header 'x-requestid' là bắt buộc và phải là một Guid hợp lệ." });
        }

        var db = httpContext.RequestServices.GetRequiredService<OrderDbContext>();
        if (await db.IdempotentRequests.FindAsync(requestId) is not null)
            return Results.Conflict(new { message = "Yêu cầu này đã được xử lý trước đó." });

        db.IdempotentRequests.Add(new IdempotentRequest { Id = requestId });
        return await next(context);
    }
}
