using Shopping_web.Extensions;
using Shopping_web.Modules.IdentityService.Endpoints;
using Shopping_web.Modules.OrderService.Endpoints;
using Shopping_web.Modules.ProductService.Endpoints;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIdentityAuthentication(builder.Configuration);
builder.Services.AddServiceDbContexts(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapProductEndpoints();
app.MapOrderEndpoints();

app.MapGet("/", () => "Hello World");

app.Run();
