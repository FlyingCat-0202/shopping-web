using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using EventBus.Contracts;
using EventBus.Extensions;
using EventBus.Infrastructure;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Product.API.Dtos;
using Product.API.IntegrationEvents.Consumers.Elastic;
using Product.Domain.Entities;
using Product.Infrastructure.Data;
using Product.Infrastructure.AISearch;

namespace Product.API.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .WithTags("Products");

        // Lấy danh sách products và categories
        group.MapGet("/", async (ProductDbContext db, ILogger<Program> logger, CancellationToken cancellationToken) =>
        {
            try
            {
                // 1. Lấy danh sách Products
                var productList = await db.Products
                    .AsNoTracking()
                    .Where(p => p.IsActive)
                    .OrderBy(p => p.Name)
                    .Select(p => new ProductResponse(
                        p.Id,
                        p.Name,
                        p.Price,
                        p.StockQuantity,
                        p.Description,
                        p.ImageUrl,
                        p.CategoryId,
                        p.Category.Name))
                    .ToListAsync(cancellationToken);

                // 2. Lấy danh sách Categories
                var categoryList = await db.Categories
                    .AsNoTracking()
                    .Select(c => new CategoryResponse(
                        c.Id,
                        c.Name,
                        c.Description
                    ))
                    .ToListAsync(cancellationToken);

                return Results.Ok(new ProductCategoryResponse(productList, categoryList));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "(ProductService) Lỗi khi truy vấn dữ liệu tổng hợp");
                return Results.Problem("Lỗi không thể lấy dữ liệu");
            }
        });

        // Lấy danh sách categories
        group.MapGet("/categories", async (ProductDbContext db, CancellationToken cancellationToken) =>
        {
            var categories = await db.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryResponse(c.Id, c.Name, c.Description))
                .ToListAsync(cancellationToken);

            return Results.Ok(categories);
        })
        .WithName("GetCategories");


        // Tìm Product theo id
        group.MapGet("/{id:guid}", async (Guid id, ProductDbContext db, ILogger<Program> logger, CancellationToken cancellationToken) =>
        {
            try
            {
                var product = await db.Products
                    .AsNoTracking()
                    .Where(p => p.Id == id && p.IsActive)
                    .Select(p => new ProductResponse(
                        p.Id,
                        p.Name,
                        p.Price,
                        p.StockQuantity,
                        p.Description,
                        p.ImageUrl,
                        p.CategoryId,
                        p.Category.Name))
                    .FirstOrDefaultAsync(cancellationToken);

                return product is not null ? Results.Ok(product) : Results.NotFound();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "(ProductService) DB lỗi không thể truy cập");
                return Results.Problem("Lỗi không thể lấy dữ liệu products");
            }
        });

        // Gộp tất cả vào 1 Endpoint chuẩn RESTful: PUT /api/products/{productId}
        group.MapPut("/{productId:guid}", async (
            Guid productId,
            [FromBody] UpdateProductRequest request, // Lấy dữ liệu từ JSON Body
            ProductDbContext db,
            IPublishEndpoint publishEndpoint,
            CancellationToken cancellationToken) =>
        {
            // 1. Kiểm tra sản phẩm có tồn tại không
            var productExists = await db.Products
                .AsNoTracking()
                .AnyAsync(p => p.Id == productId && p.IsActive, cancellationToken);

            if (!productExists)
                return Results.NotFound(new { message = "Sản phẩm không tồn tại hoặc đã ngừng kinh doanh." });

            // 2. (Tùy chọn) Kiểm tra Category nếu categoryId bị đổi
            var categoryExists = await db.Categories
                .AsNoTracking()
                .AnyAsync(c => c.Id == request.CategoryId, cancellationToken);

            if (!categoryExists)
                return Results.NotFound(new { message = "Danh mục không tồn tại." });

            // 3. Đóng gói dữ liệu gửi lên Exchange
            var updateMsg = new UpdateProductRequest(
                productId,
                request.Name,
                request.Price,
                request.StockQuantity,
                request.IsActive,
                request.Description,
                request.ImgUrl,
                request.CategoryId
            );

            await SendMessage(updateMsg, publishEndpoint, cancellationToken);

            // 4. Lưu vào Outbox
            await db.SaveChangesAsync(cancellationToken);

            return Results.Accepted(null, new
            {
                message = "Yêu cầu cập nhật sản phẩm đã được đưa vào hàng đợi.",
                ProductId = productId
            });
        })
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .AddEndpointFilter<ValidationFilter<UpdateProductRequest>>() // Nếu bạn có fluent validation
        .AddEndpointFilter<IdempotencyFilter>();

        // Xóa Product theo id
        group.MapDelete("/{id:guid}", async (
            Guid id,
            ProductDbContext db,
            IPublishEndpoint publishEndpoint,
            CancellationToken cancellationToken) =>
        {
            var product = await db.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive, cancellationToken);

            if (product is null)
                return Results.NotFound(new { message = "Sản phẩm không tồn tại hoặc đã được xóa." });

            await SendMessage(
                new DeleteProductRequest(id),
                publishEndpoint,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Accepted(null, new { message = "Yêu cầu xóa/ẩn sản phẩm đã được đưa vào hàng đợi.", ProductId = id });
        })
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .AddEndpointFilter<IdempotencyFilter>();

        group.MapPost("/categories", async (
            CategoryRequest request,
            ProductDbContext db,
            CancellationToken cancellationToken) =>
        {
            var categoryName = request.Name.Trim();
            var normalizedName = categoryName.ToLowerInvariant();

            var categoryExists = await db.Categories
                .AsNoTracking()
                .AnyAsync(c => c.Name.ToLower() == normalizedName, cancellationToken);

            if (categoryExists)
                return Results.Conflict(new { message = "Danh mục đã tồn tại." });

            var category = new Category
            {
                Name = categoryName,
                Description = string.IsNullOrWhiteSpace(request.Description)
                    ? null
                    : request.Description.Trim()
            };

            db.Categories.Add(category);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/products/categories/{category.Id}",
                new CategoryResponse(category.Id, category.Name, category.Description));
        })
        .AddEndpointFilter<ValidationFilter<CategoryRequest>>()
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .WithName("CreateCategory")
        .AddEndpointFilter<IdempotencyFilter>();


        // Tạo một sản phẩm với id ngẫu nhiên
        group.MapPost("/", async (
            ProductRequest request,
            ProductDbContext db,
            IPublishEndpoint publishEndpoint,
            CancellationToken cancellationToken) =>
        {
            var category = await db.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken);

            if (category is null)
                return Results.BadRequest(new { message = "Danh mục không hợp lệ." });

            var msg = new CreateProductRequest(
                request.Name,
                request.Price,
                request.StockQuantity,
                category.Id,
                request.Description,
                request.ImageUrl,
                request.IsActive);

            await SendMessage(msg, publishEndpoint, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Accepted(null, new { message = "Yêu cầu tạo sản phẩm đã được đưa vào hàng đợi." });
        })
        .AddEndpointFilter<ValidationFilter<ProductRequest>>()
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .AddEndpointFilter<IdempotencyFilter>();

        group.MapGet("/search", async Task<IResult> (
            [AsParameters] SearchProductRequest request,
            ProductDbContext db,
            ElasticsearchClient elasticClient,
            IAiEmbeddingService _aiEmbeddingService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var page = Math.Max(request.Page, 1);
            var pageSize = Math.Clamp(request.PageSize, 1, 50);
            var keyword = request.Keyword?.Trim() ?? string.Empty;
            var from = (page - 1) * pageSize;

            var existingCategories = await db.Categories
                                             .Select(p => new { p.Name, p.Description })
                                             .ToListAsync(cancellationToken);

            // 2. Kiểm tra xem người dùng có gõ tên danh mục nào không
            string? targetCategory = null;

            if (request.Keyword is not null)
            {
                // 1. Chuẩn hóa keyword và tách thành danh sách các từ vựng (chỉ làm 1 lần ngoài vòng lặp)
                string normalizedKeyword = EndpointHelpers.NormalizeVietnamese(request.Keyword);
                char[] separators = new[] { ' ', ',', '.', ';', '?', '!', '-', '_' };

                string[] keywordWords = normalizedKeyword.Split(separators, StringSplitOptions.RemoveEmptyEntries);

                // 2. Dùng LINQ để tính điểm
                var bestCategoryMatch = existingCategories
                    .Select(cat =>
                    {
                        string normalizedName = EndpointHelpers.NormalizeVietnamese(cat.Name ?? "");
                        string normalizedDesc = EndpointHelpers.NormalizeVietnamese(cat.Description ?? "");

                        // --- TIÊU CHÍ 1: Trùng khớp nguyên cụm hoàn toàn (Ưu tiên tuyệt đối) ---
                        if (normalizedName == normalizedKeyword)
                        {
                            return new { Category = cat, Score = 100 };
                        }

                        // --- TIÊU CHÍ 2: Đếm từ khớp chính xác bằng HashSet ---

                        // Đưa Name thành HashSet (Dùng StringComparer để không phân biệt hoa thường)
                        HashSet<string> nameWords = normalizedName
                            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        // Đưa Description thành HashSet
                        HashSet<string> descWords = normalizedDesc
                            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        // Đếm số từ của Keyword xuất hiện trong Name và Description
                        int matchedWordsInName = keywordWords.Count(w => nameWords.Contains(w));
                        int matchedWordsInDesc = keywordWords.Count(w => descWords.Contains(w));

                        return new { Category = cat, Score = Math.Max(matchedWordsInName, matchedWordsInDesc)};
                    })
                    .Where(x => x.Score > 0)          // Chỉ lấy những category có điểm
                    .OrderByDescending(x => x.Score)  // Lấy điểm cao nhất đưa lên đầu
                    .FirstOrDefault();

                // 3. Gán kết quả
                if (bestCategoryMatch != null)
                {
                    targetCategory = bestCategoryMatch.Category.Name;
                }
            }

            try
            {
                if (string.IsNullOrWhiteSpace(request.Keyword))
                {
                    return await SearchProductsFromDatabase(db, keyword, page, pageSize, cancellationToken);
                }

                // 1. Lấy vector của từ khóa từ AI Model 
                float[] queryVector = await _aiEmbeddingService.GetVectorAsync(request.Keyword);
                var normalizedQueryVector = ElasticProductIndex.NormalizeVector(queryVector, logger, $"search keyword '{keyword}'");
                var useVectorSearch = normalizedQueryVector is not null;

                // 2. Thực hiện Hybrid Search
                var searchResponse = await elasticClient.SearchAsync<ProductEsDocument>(s => s
                    .Indices(ElasticProductIndex.Name)
                    .From(from)
                    .Size(request.PageSize)
                    .Query(q => q
                        .Bool(b =>
                        {
                            // CẤU HÌNH SHOULD: Vector search & BM25
                            if (useVectorSearch)
                            {
                                b.Should(
                                    sh1 => sh1.ScriptScore(ss => ss
                                        .Query(q2 => q2.MatchAll())
                                        .Script(new Script
                                        {
                                            Source = "doc['nameEmbeddingVector'].isEmpty() ? 0.0 : cosineSimilarity(params.queryVector, 'nameEmbeddingVector') + 1.0",
                                            Params = new Dictionary<string, object>
                                            {
                                                { "queryVector", normalizedQueryVector! }
                                            }
                                        })
                                    ),

                                    sh2 => sh2.MultiMatch(mm => mm
                                        .Query(request.Keyword)
                                        .Fields(new[] { "name^5", "categoryName" })
                                        .Fuzziness(new Fuzziness("AUTO"))
                                    )
                                );
                            }
                            else
                            {
                                b.Should(sh => sh.MultiMatch(mm => mm
                                    .Query(request.Keyword)
                                    .Fields(new[] { "name^5", "categoryName" })
                                    .Fuzziness(new Fuzziness("AUTO"))
                                ));
                            }

                            // CẤU HÌNH FILTER ĐỘNG
                            if (!string.IsNullOrEmpty(targetCategory))
                            {
                                b.Filter(
                                    f1 => f1.Term(t => t.Field(p => p.IsActive).Value(true)),
                                    f2 => f2.Term(t => t.Field(p => p.CategoryName).Value(targetCategory))
                                );
                            }
                            else
                            {
                                b.Filter(
                                    f => f.Term(t => t.Field(p => p.IsActive).Value(true))
                                );
                            }

                            b.MinimumShouldMatch(1);
                        })
                    ),
                    cancellationToken
                );

                if (searchResponse.IsValidResponse)
                {
                    var items = searchResponse.Documents.Select(doc => new ProductResponse(
                        Id: doc.Id,
                        Name: doc.Name,
                        Price: doc.Price,
                        StockQuantity: 0,
                        Description: doc.Description,
                        ImageUrl: doc.ImageUrl,
                        CategoryId: 0,
                        CategoryName: doc.CategoryName
                    ));

                    return Results.Ok(new
                    {
                        TotalItems = searchResponse.Total,
                        CurrentPage = page,
                        Items = items
                    });
                }

                logger.LogWarning(
                    "Elasticsearch search failed. Falling back to database search. Details: {DebugInformation}",
                    searchResponse.DebugInformation);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Elasticsearch search threw an exception. Falling back to database search.");
            }

            // Fallback cuối cùng nếu Elastic lỗi
            return await SearchProductsFromDatabase(db, keyword, page, pageSize, cancellationToken);
        });
    }



    private static async Task SendMessage<T>(
        T msg,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        await publishEndpoint.Publish(msg!, cancellationToken);
    }

    private static async Task<IResult> SearchProductsFromDatabase(
        ProductDbContext db,
        string keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = db.Products
            .AsNoTracking()
            .Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{keyword}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, pattern) ||
                (p.Description != null && EF.Functions.ILike(p.Description, pattern)) ||
                EF.Functions.ILike(p.Category.Name, pattern));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductResponse(
                p.Id,
                p.Name,
                p.Price,
                p.StockQuantity,
                p.Description,
                p.ImageUrl,
                p.CategoryId,
                p.Category.Name))
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            TotalItems = totalItems,
            CurrentPage = page,
            Items = items
        });
    }
}
