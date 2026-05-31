using ServiceDefault;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddTransforms(context =>
    {
        context.AddRequestTransform(requestCtx =>
        {
            var request = requestCtx.HttpContext.Request;

            requestCtx.ProxyRequest.Headers.Remove("Origin");
            requestCtx.ProxyRequest.Headers.Remove("X-Forwarded-Host");
            requestCtx.ProxyRequest.Headers.Remove("X-Forwarded-Proto");
            requestCtx.ProxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-Host", request.Host.Value);
            requestCtx.ProxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-Proto", request.Scheme);

            return ValueTask.CompletedTask;
        });
    });

builder.AddApiServiceDefaults();

builder.Services.AddServiceDiscovery();

builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddServiceDiscovery();
});

var app = builder.Build();

app.UseApiServiceDefaults();

app.MapApiHealthChecks();
app.MapReverseProxy();

app.Run();
