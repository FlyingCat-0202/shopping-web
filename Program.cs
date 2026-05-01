using Shopping_web.Modules.IdentityService.Endpoints; 
using Shopping_web.Modules.OrderService.Endpoints;
using Shopping_web.Modules.ProductService.Endpoints;
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "Hello World");

app.Run();
