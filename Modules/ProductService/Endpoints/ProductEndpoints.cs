using Microsoft.EntityFrameworkCore;
using Shopping_web.Modules.ProductService.DTOs;
using Shopping_web.Modules.ProductService.Models;

namespace Shopping_web.Modules.ProductService.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        // TEST THOI NHE !
        var group = app.MapGroup("/api/products").WithTags("Products");

        group.MapGet("/", async (AppDbContext db) =>
        {
            var products = await db.Products
                .Include(p => p.Category)
                .Select(p => new ProductResponse(
                    p.Id, 
                    p.Name, 
                    p.Price, 
                    p.StockQuantity, 
                    p.Category.Name))
                .ToListAsync();
            return Results.Ok(products);
        });

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            return await db.Products
                .Include(p => p.Category)
                .Where(p => p.Id == id)
                .Select(p => new ProductResponse(p.Id, p.Name, p.Price, p.StockQuantity, p.Category.Name))
                .FirstOrDefaultAsync() is ProductResponse product
                    ? Results.Ok(product)
                    : Results.NotFound();
        });

        group.MapPost("/", async (CreateProductRequest request, AppDbContext db) =>
        {
            var product = new Product
            {
                Name = request.Name,
                Price = request.Price,
                StockQuantity = request.StockQuantity,
                CategoryId = request.CategoryId
            };

            db.Products.Add(product);
            await db.SaveChangesAsync();

            return Results.Created($"/api/products/{product.Id}", product);
        });
    }
}