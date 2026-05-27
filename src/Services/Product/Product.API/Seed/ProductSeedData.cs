using EventBus.Contracts; // Thêm thư viện chứa ProductCreatedEvent
using MassTransit;        // Thêm thư viện MassTransit
using Microsoft.EntityFrameworkCore;
using Product.Domain.Entities;
using Product.Infrastructure.Data;
using ProductEntity = Product.Domain.Entities.Product;

namespace Product.API.Seed;

public static class ProductSeedData
{
    // Thay đổi tham số truyền vào của hàm SeedAsync
    public static async Task SeedAsync(
        ProductDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        ILogger logger)
    {
        var categoryDefinitions = new Dictionary<string, string>
        {
            ["Áo"] = "Áo thun, áo sơ mi, áo khoác và hoodie.",
            ["Quần"] = "Quần jeans, quần kaki, quần short và jogger.",
            ["Giày"] = "Giày sneaker, giày chạy bộ và sandal.",
            ["Phụ kiện"] = "Túi, nón, ví, thắt lưng và phụ kiện thời trang.",
            ["Đồ lót"] = "Quần xì, quần lót, quần xilip, áo vú, áo lá, áo ngực."
        };

        await EnsureCategoriesAsync(dbContext, categoryDefinitions);

        var categoryNames = categoryDefinitions.Keys.ToList();
        var categoryByName = await dbContext.Categories
            .Where(c => categoryNames.Contains(c.Name))
            .ToDictionaryAsync(c => c.Name, c => c.Id);

        var existingProductNames = await dbContext.Products
            .Select(p => p.Name)
            .ToListAsync();
        var existingProductNameSet = new HashSet<string>(existingProductNames, StringComparer.OrdinalIgnoreCase);

        var productsToAdd = BuildSeedProducts()
            .Where(seed => !existingProductNameSet.Contains(seed.Name))
            .Select(seed => new ProductEntity
            {
                Name = seed.Name,
                Price = seed.Price,
                StockQuantity = seed.StockQuantity,
                CategoryId = categoryByName[seed.CategoryName],
                Description = seed.Description,
                ImageUrl = seed.ImageUrl,
                IsActive = true
            })
            .ToList();

        if (productsToAdd.Count == 0)
        {
            logger.LogInformation("Product seed skipped because all seed products already exist.");
            return;
        }

        // 2. Thêm sản phẩm vào ChangeTracker và Lưu lần 1
        dbContext.Products.AddRange(productsToAdd);
        await dbContext.SaveChangesAsync();

        // 3. Vòng lặp bắn Event cho từng sản phẩm mới tạo
        foreach (var product in productsToAdd)
        {
            var categoryName = categoryByName.FirstOrDefault(c => c.Value == product.CategoryId).Key ?? "Unknown";

            var eventMsg = new ProductCreatedEvent(
                product.Id,
                product.Name,
                product.Description ?? "",
                product.Price,
                categoryName,
                product.IsActive
            );

            await publishEndpoint.Publish(eventMsg);
        }

        // 4. Lưu lần 2: Đẩy toàn bộ Event trong Outbox xuống Database
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {ProductCount} products and published events to EventBus.", productsToAdd.Count);
    }

    private static async Task EnsureCategoriesAsync(
        ProductDbContext dbContext,
        IReadOnlyDictionary<string, string> categoryDefinitions)
    {
        var categoryNames = categoryDefinitions.Keys.ToList();
        var existingCategoryNames = await dbContext.Categories
            .Where(c => categoryNames.Contains(c.Name))
            .Select(c => c.Name)
            .ToListAsync();

        var existingCategoryNameSet = new HashSet<string>(existingCategoryNames, StringComparer.OrdinalIgnoreCase);
        var categoriesToAdd = categoryDefinitions
            .Where(c => !existingCategoryNameSet.Contains(c.Key))
            .Select(c => new Category
            {
                Name = c.Key,
                Description = c.Value
            })
            .ToList();

        if (categoriesToAdd.Count == 0)
            return;

        dbContext.Categories.AddRange(categoriesToAdd);
        await dbContext.SaveChangesAsync();
    }

    private static IEnumerable<ProductSeed> BuildSeedProducts()
    {
        foreach (var seed in CreateSeeds(
            "Áo",
            [
                "Áo thun trắng basic",
                "Áo thun đen basic",
                "Áo thun xanh navy",
                "Áo thun oversize xám",
                "Áo polo trắng",
                "Áo polo đen",
                "Áo polo xanh rêu",
                "Áo sơ mi xanh Oxford",
                "Áo sơ mi trắng công sở",
                "Áo sơ mi linen be",
                "Áo khoác denim xanh",
                "Áo khoác bomber đen",
                "Áo hoodie xám",
                "Áo hoodie đen",
                "Áo sweater kem",
                "Áo cardigan nâu",
                "Áo tanktop thể thao",
                "Áo dài tay sọc",
                "Áo thun graphic Mei",
                "Áo thun cotton premium",
                "Áo sơ mi caro đỏ",
                "Áo khoác gió xanh",
                "Áo vest casual đen",
                "Áo len cổ tròn",
                "Áo khoác varsity"
            ],
            120000,
            12000,
            28,
            "Sản phẩm nhóm áo dùng để test catalog, cart và order.",
            "https://images.unsplash.com/photo-1521572163474-6864f9cf17ab"))
        {
            yield return seed;
        }

        foreach (var seed in CreateSeeds(
            "Quần",
            [
                "Quần jeans xanh regular",
                "Quần jeans đen slim",
                "Quần jeans xanh nhạt",
                "Quần kaki be",
                "Quần kaki đen",
                "Quần short kaki đen",
                "Quần short jeans xanh",
                "Quần jogger xám",
                "Quần jogger đen",
                "Quần tây đen basic",
                "Quần tây xanh navy",
                "Quần linen trắng",
                "Quần cargo xanh rêu",
                "Quần cargo đen",
                "Quần thể thao co giãn",
                "Quần baggy be",
                "Quần ống rộng nâu",
                "Quần chino xám",
                "Quần chino kem",
                "Quần jeans rách nhẹ",
                "Quần short thể thao",
                "Quần kaki nâu",
                "Quần denim đen loose",
                "Quần sweatpants xám",
                "Quần outdoor chống nước"
            ],
            180000,
            15000,
            24,
            "Sản phẩm nhóm quần dùng để test filter, stock và checkout.",
            "https://images.unsplash.com/photo-1542272604-787c3835535d"))
        {
            yield return seed;
        }

        foreach (var seed in CreateSeeds(
            "Giày",
            [
                "Giày sneaker trắng",
                "Giày sneaker đen",
                "Giày chạy bộ đen",
                "Giày chạy bộ xanh",
                "Giày canvas trắng",
                "Giày canvas đen",
                "Giày slip-on be",
                "Giày thể thao cổ cao",
                "Giày thể thao cổ thấp",
                "Giày da casual nâu",
                "Giày da casual đen",
                "Sandal quai ngang đen",
                "Sandal quai chéo nâu",
                "Dép slide trắng",
                "Dép slide đen",
                "Giày hiking xám",
                "Giày training đỏ",
                "Giày tennis trắng",
                "Giày lifestyle be",
                "Giày platform trắng",
                "Giày loafer đen",
                "Giày boot da nâu",
                "Giày boot cổ thấp đen",
                "Giày chạy trail xanh rêu",
                "Giày sneaker retro"
            ],
            320000,
            22000,
            18,
            "Sản phẩm nhóm giày dùng để test giá cao hơn và tồn kho thấp hơn.",
            "https://images.unsplash.com/photo-1549298916-b41d501d3772"))
        {
            yield return seed;
        }

        foreach (var seed in CreateSeeds(
            "Phụ kiện",
            [
                "Nón baseball be",
                "Nón baseball đen",
                "Nón bucket trắng",
                "Nón bucket xanh rêu",
                "Túi tote canvas",
                "Túi tote đen",
                "Túi đeo chéo nhỏ",
                "Balo laptop đen",
                "Ví da nâu",
                "Ví da đen",
                "Thắt lưng da nâu",
                "Thắt lưng da đen",
                "Vớ cổ cao trắng",
                "Vớ cổ cao đen",
                "Khăn bandana xanh",
                "Khăn len xám",
                "Kính mát gọng đen",
                "Kính mát gọng nâu",
                "Đồng hồ dây da",
                "Đồng hồ dây kim loại",
                "Móc khóa MeiMei",
                "Bình nước thể thao",
                "Túi gym du lịch",
                "Bao da điện thoại",
                "Dây đeo thẻ canvas"
            ],
            60000,
            9000,
            35,
            "Sản phẩm phụ kiện dùng để test giỏ hàng với nhiều món nhỏ.",
            "https://images.unsplash.com/photo-1521369909029-2afed882baee"))
        {
            yield return seed;
        }

        foreach (var seed in CreateSeeds(
            "Đồ lót",
            [
                "Quần xì nam cotton",
                "Quần lót nam boxer",
                "Quần lót nam tam giác",
                "Quần lót nữ trơn",
                "Quần lót nữ ren",
                "Quần xilip cạp cao",
                "Quần xilip không viền",
                "Áo vú mút mỏng",
                "Áo vú nâng ngực",
                "Áo vú thể thao",
                "Áo vú không gọng",
                "Áo ngực ren cao cấp",
                "Áo ngực cotton basic",
                "Áo ngực bralette",
                "Áo lá học sinh",
                "Áo lá croptop",
                "Áo lá hai dây",
                "Quần lót thun lạnh",
                "Quần lót su đúc",
                "Quần sịp đùi nam",
                "Quần lót tàng hình",
                "Áo ngực quây",
                "Áo ngực dán",
                "Áo lót mặc trong",
                "Set đồ lót cotton"
            ],
            40000,
            5000,
            50,
            "Sản phẩm nhóm đồ lót mặc trong, thoải mái, thấm hút mồ hôi.",
            "https://images.unsplash.com/photo-1596561073167-93043c983a00"))
        {
            yield return seed;
        }
    }

    private static IEnumerable<ProductSeed> CreateSeeds(
        string categoryName,
        IReadOnlyList<string> names,
        decimal basePrice,
        decimal priceStep,
        int baseStock,
        string description,
        string imageUrl)
    {
        for (var i = 0; i < names.Count; i++)
        {
            yield return new ProductSeed(
                names[i],
                basePrice + priceStep * i,
                baseStock + i % 17,
                categoryName,
                description,
                imageUrl);
        }
    }

    private sealed record ProductSeed(
        string Name,
        decimal Price,
        int StockQuantity,
        string CategoryName,
        string Description,
        string ImageUrl);
}