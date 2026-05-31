using EventBus.Contracts;
using Bogus;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.Domain.Entities;
using Product.Infrastructure.Data;
using ProductEntity = Product.Domain.Entities.Product;

namespace DbSeeder;

public static class ProductGenerator
{
    private static readonly Dictionary<string, string[]> Subcategories = new()
    {
        ["Áo"] = ["Áo thun cotton", "Áo polo pique", "Áo sơ mi oxford", "Áo sơ mi linen", "Áo len cổ tròn", "Áo sweater", "Áo thun dài tay", "Áo tank top"],
        ["Áo khoác"] = ["Áo khoác denim", "Áo khoác bomber", "Áo khoác gió", "Áo hoodie fleece", "Áo khoác varsity", "Áo gile phao", "Áo blazer casual"],
        ["Quần"] = ["Quần jeans dáng suông", "Quần jeans slim fit", "Quần kaki chino", "Quần jogger thể thao", "Quần tây công sở", "Quần short kaki", "Quần short jeans", "Quần cargo túi hộp"],
        ["Giày"] = ["Giày sneaker trắng", "Giày chạy bộ nhẹ", "Giày canvas cổ thấp", "Giày boot da nam", "Giày loafer thanh lịch", "Sandal quai ngang", "Dép slide êm ái"],
        ["Túi & phụ kiện"] = ["Balo laptop chống nước", "Túi tote canvas", "Túi đeo chéo mini", "Ví da nam bifold", "Thắt lưng da bò", "Nón baseball basic", "Đồng hồ classic dây da", "Kính mát thời trang", "Vớ cổ cao cotton"],
        ["Đồ thể thao"] = ["Áo thun quickdry thể thao", "Quần short chạy bộ 2 lớp", "Quần legging co giãn", "Túi gym trống du lịch", "Bình nước giữ nhiệt thể thao"],
        ["Đồ lót"] = ["Quần lót nam boxer", "Quần lót nữ su đúc", "Áo ngực cotton basic", "Áo lá học sinh mềm mại", "Quần sịp ren", "Quần xilip nam tính"],
        ["Đồ bộ"] = ["Đồ bộ mặc nhà cotton", "Đồ bộ mặc nhà lụa", "Đồ bộ mặc nhà nỉ", "Đồ bộ mặc nhà thun lạnh", "Đồ bộ mặc nhà oversized", "Đồ bộ mặc nhà pijama"]
    };

    private static readonly string[] ProductBrands =
    [
        "Sech Studio", "Northline", "Urban Form", "Mori", "Lumi Wear", "Coastal", "Aster",
        "Routine Lab", "Nexa", "Bloom Atelier", "Terra Mode", "Maison Daily", "Arc Fit"
    ];

    private static readonly string[] Materials =
    [
        "cotton compact", "linen blend", "modal mềm", "denim stretch", "canvas dày",
        "fleece nhẹ", "pique thoáng khí", "poly quick-dry", "da tổng hợp cao cấp",
        "rib co giãn", "lụa satin", "nỉ bông"
    ];

    private static readonly string[] Fits =
    [
        "regular fit", "relaxed fit", "slim fit", "oversized", "boxy fit",
        "straight fit", "tapered fit", "cropped fit", "athletic fit"
    ];

    private static readonly string[] Collections =
    [
        "City Basics", "Weekend Edit", "Workday Comfort", "Summer Ease", "Travel Ready",
        "Active Daily", "Minimal Wardrobe", "Soft Living", "Street Essential"
    ];

    private static readonly string[] Adjectives =
    [
        "Premium", "Basic", "Everyday", "Classic", "Sporty", "Oversized", "Linen", "Comfort", 
        "Streetwear", "Retro", "Minimalist", "Vintage", "Tech", "Active", "Ultra", "Dynamic", 
        "Soft", "Air", "Luxe", "Flex", "Essential", "Modern", "Timeless", "Versatile", "Signature", "Bold", "Sleek", "Edgy", "Relaxed", "Tailored", "Lightweight", "Durable", "Breathable", "Cozy", "Chic", "Functional", "Innovative", "Sustainable", "Classic Fit", "Slim Fit", "Regular Fit", "Relaxed Fit", "Athletic Fit", "Tapered Fit", "Straight Fit", "Skinny Fit", "Loose Fit", "Cropped Fit", "High-Waisted", "Mid-Rise", "Low-Rise", "Ankle-Length", "Full-Length", "Short Sleeve", "Long Sleeve", "Sleeveless", "Hooded", "Collared", "Button-Up", "Pullover", "Zip-Up", "V-Neck", "Crew Neck", "Off-Shoulder", "Raglan Sleeve", "Drop Shoulder", "Dolman Sleeve", "Balloon Sleeve", "Bishop Sleeve", "Lantern Sleeve", "Puff Sleeve", "Bell Sleeve", "Kimono Sleeve", "Cap Sleeve", "Flutter Sleeve", "Bardot Neckline", "Sweetheart Neckline", "Scoop Neckline", "Boat Neckline", "Halter Neckline", "Asymmetrical Neckline", "Cowl Neckline", "Turtleneck", "Mock Neck", "Scoop Back", "Keyhole Back", "Open Back", "Strappy Back", "Racerback", "Crossback", "Cutout Back", "Tie-Back", "Peplum Waist", "Empire Waist", "Drop Waist", "High-Low Hemline", "Asymmetrical Hemline", "Curved Hemline", "Straight Hemline", "Raw Hemline", "Frayed Hemline", "Side Slit", "Front Slit", "Back Slit", "Wrap Style", "Draped Style", "Layered Style", "Colorblock Style", "Printed Style", "Embroidered Style", "Textured Style", "Ribbed Style", "Striped Style", "Plaid Style", "Floral Style", "Geometric Style", "Abstract Style", "Animal Print Style", "Camouflage Style", "Tie-Dye Style", "Ombre Style", "Patchwork Style", "Distressed Style", "Vintage Wash", "Acid Wash", "Stone Wash", "Bleached Wash", "Raw Denim", "Selvedge Denim", "Stretch Denim", "Rigid Denim"
    ];

    private static readonly string[] Colors =
    [
        "Đen", "Trắng", "Navy", "Xám Melange", "Be Cát", "Xanh Rêu", "Nâu Đất", "Đỏ Burgundy", 
        "Vàng Mustard", "Cam Đất", "Xanh Dương", "Hồng Pastel", "Tím Than", "Xanh Mint", "Kem Sữa", "Xám Tro", "Đỏ Gạch", "Xanh Olive", "Vàng Chanh", "Cam Cháy", "Xanh Lam", "Hồng Phấn", "Tím Oải Hương", "Xanh Ngọc", "Kem Mơ", "Xám Khói", "Đỏ Ruby", "Xanh Peacock", "Vàng Mùt", "Cam San Hô", "Xanh Denim", "Hồng Cánh Sen", "Tím Violet", "Xanh Aqua", "Kem Latte", "Xám Xi Măng", "Đỏ Đô", "Xanh Teal", "Vàng Pha Lê", "Cam Gạch", "Xanh Indigo", "Hồng Fuchsia", "Tím Amethyst", "Xanh Biển Sáng", "Kem Vanilla", "Xám Bạc", "Đỏ Hồng Ngọc", "Xanh Peacock Sáng", "Vàng Chanh Tươi", "Cam San Hô Nhạt", "Xanh Denim Nhạt", "Hồng Cánh Sen Nhạt", "Tím Violet Nhạt", "Xanh Aqua Nhạt", "Kem Latte Nhạt", "Xám Xi Măng Nhạt", "Đỏ Đô Nhạt", "Xanh Teal Nhạt", "Vàng Pha Lê Nhạt", "Cam Gạch Nhạt", "Xanh Indigo Nhạt", "Hồng Fuchsia Nhạt", "Tím Amethyst Nhạt", "Xanh Biển Sáng Nhạt", "Kem Vanilla Nhạt", "Xám Bạc Nhạt", "Đỏ Hồng Ngọc Nhạt", "Xanh Peacock Sáng Nhạt", "Vàng Chanh Tươi Nhạt", "Cam San Hô Nhạt", "Xanh Denim Nhạt", "Hồng Cánh Sen Nhạt", "Tím Violet Nhạt", "Xanh Aqua Nhạt", "Kem Latte Nhạt", "Xám Xi Măng Nhạt", "Đỏ Đô Nhạt", "Xanh Teal Nhạt", "Vàng Pha Lê Nhạt", "Cam Gạch Nhạt", "Xanh Indigo Nhạt", "Hồng Fuchsia Nhạt", "Tím Amethyst Nhạt", "Xanh Biển Sáng Nhạt", "Kem Vanilla Nhạt", "Xám Bạc Nhạt", "Đỏ Hồng Ngọc Nhạt", "Xanh Peacock Sáng Nhạt", "Vàng Chanh Tươi Nhạt", "Cam San Hô Nhạt", "Xanh Denim Nhạt", "Hồng Cánh Sen Nhạt", "Tím Violet Nhạt", "Xanh Aqua Nhạt", "Kem Latte Nhạt", "Xám Xi Măng Nhạt", "Đỏ Đô Nhạt", "Xanh Teal Nhạt", "Vàng Pha Lê Nhạt", "Cam Gạch Nhạt", "Xanh Indigo Nhạt", "Hồng Fuchsia Nhạt", "Tím Amethyst Nhạt", "Xanh Biển Sáng Nhạt", "Kem Vanilla Nhạt", "Xám Bạc Nhạt", "Đỏ Hồng Ngọc Nhạt", "Xanh Peacock Sáng Nhạt", "Vàng Chanh Tươi Nhạt", "Cam San Hô Nhạt", "Xanh Denim Nhạt", "Hồng Cánh Sen Nhạt", "Tím Violet Nhạt", "Xanh Aqua Nhạt", "Kem Latte Nhạt", "Xám Xi Măng Nhạt", "Đỏ Đô Nhạt", "Xanh Teal Nhạt", "Vàng Pha Lê Nhạt", "Cam Gạch Nhạt", "Xanh Indigo Nhạt", "Hồng Fuchsia Nhạt", "Tím Amethyst Nhạt", "Xanh Biển Sáng Nhạt", "Kem Vanilla Nhạt", "Xám Bạc Nhạt"
    ];

    private static readonly Dictionary<string, string[]> UnsplashImages = new()
    {
        ["Áo"] =
        [
            "https://images.unsplash.com/photo-1521572163474-6864f9cf17ab?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1523381210434-271e8be1f52b?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1598032895397-b9472444bf93?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1603252109303-2751441dd157?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1618354691373-d851c5c3a990?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1618354691792-d1d42acfd860?auto=format&fit=crop&w=900&q=80"
        ],
        ["Áo khoác"] =
        [
            "https://images.unsplash.com/photo-1543076447-215ad9ba6923?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1551028719-00167b16eac5?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1542291026-7eec264c27ff?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1556821840-3a63f95609a7?auto=format&fit=crop&w=900&q=80"
        ],
        ["Quần"] =
        [
            "https://images.unsplash.com/photo-1542272604-787c3835535d?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1473966968600-fa801b869a1a?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1506629905607-c52b54afcc81?auto=format&fit=crop&w=900&q=80"
        ],
        ["Giày"] =
        [
            "https://images.unsplash.com/photo-1549298916-b41d501d3772?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1542291026-7eec264c27ff?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1525966222134-fcfa99b8ae77?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1614252369475-531eba835eb1?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1603487742131-4160ec999306?auto=format&fit=crop&w=900&q=80"
        ],
        ["Túi & phụ kiện"] =
        [
            "https://images.unsplash.com/photo-1553062407-98eeb64c6a62?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1542295669297-4d352b042bca?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1627123424574-724758594e93?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1521369909029-2afed882baee?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1523275335684-37898b6baf30?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1624222247344-550fb60583dc?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1511499767150-a48a237f0083?auto=format&fit=crop&w=900&q=80"
        ],
        ["Đồ thể thao"] =
        [
            "https://images.unsplash.com/photo-1518611012118-696072aa579a?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1602143407151-7111542de6e8?auto=format&fit=crop&w=900&q=80"
        ],
        ["Đồ lót"] =
        [
            "https://images.unsplash.com/photo-1596561073167-93043c983a00?auto=format&fit=crop&w=900&q=80"
        ],
        ["Đồ bộ"] =
        [
            "https://images.unsplash.com/photo-1600185364417-9bafcaa1e8c6?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1600185364417-9bafcaa1e8c6?auto=format&fit=crop&w=900&q=80",
            "https://images.unsplash.com/photo-1600185364417-9bafcaa1e8c6?auto=format&fit=crop&w=900&q=80"
        ]
    };

    public static async Task GenerateProductsAsync(
        ProductDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        int totalToSeed)
    {
        Console.WriteLine($"Starting seed of {totalToSeed} products...");

        var categoryMap = await SeedCategoriesAsync(dbContext);

        var existingNames = await dbContext.Products
            .Select(p => p.Name)
            .ToListAsync();
        var existingNamesSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        var productsToAdd = new List<ProductEntity>();
        var random = new Random();
        var faker = new Faker("vi");
        var categoryNames = Subcategories.Keys.ToArray();

        int generatedCount = 0;
        int maxAttempts = totalToSeed * 10;
        int attempts = 0;

        while (generatedCount < totalToSeed && attempts < maxAttempts)
        {
            attempts++;
            var categoryName = faker.PickRandom(categoryNames);
            var categoryId = categoryMap[categoryName];

            var subcat = faker.PickRandom(Subcategories[categoryName]);
            var style = faker.PickRandom(Adjectives);
            var color = faker.PickRandom(Colors);
            var brand = faker.PickRandom(ProductBrands);
            var material = faker.PickRandom(Materials);
            var fit = faker.PickRandom(Fits);
            var collection = faker.PickRandom(Collections);
            var sku = faker.Random.Guid().ToString("N")[..8].ToUpperInvariant();

            string name = GenerateProductName(brand, subcat, material, fit, color, sku);

            if (existingNamesSet.Contains(name))
            {
                continue;
            }

            existingNamesSet.Add(name);

            decimal price = GetLogicalPrice(subcat, random);
            int stock = faker.Random.Bool(0.08f) ? 0 : random.Next(1, 150);

            var images = UnsplashImages[categoryName];
            var imageUrl = images[random.Next(images.Length)];

            string description = GenerateDescription(faker, name, subcat, material, fit, color, collection);

            var product = new ProductEntity
            {
                Name = name,
                Price = price,
                StockQuantity = stock,
                CategoryId = categoryId,
                Description = description,
                ImageUrl = imageUrl,
                IsActive = true
            };

            productsToAdd.Add(product);
            generatedCount++;
        }

        if (productsToAdd.Count == 0)
        {
            Console.WriteLine("All generated products already exist in the database. Seeding skipped.");
            return;
        }

        // ── Fix 1: Lưu DB theo từng chunk 5.000 rows thay vì 1 transaction khổng lồ ──
        // Mỗi chunk là 1 transaction nhỏ → tránh OOM, tránh Postgres lock lâu,
        // và EF Core không phải track 1M entities cùng lúc trong RAM.
        const int dbBatchSize = 5_000;
        int savedCount = 0;
        Console.WriteLine($"Writing {productsToAdd.Count} products to database in chunks of {dbBatchSize}...");

        foreach (var chunk in productsToAdd.Chunk(dbBatchSize))
        {
            dbContext.Products.AddRange(chunk);
            await dbContext.SaveChangesAsync();

            // Bắt buộc: xóa tracked entities khỏi ChangeTracker sau mỗi chunk
            // để EF Core không tích lũy object graph trong suốt vòng lặp.
            dbContext.ChangeTracker.Clear();

            savedCount += chunk.Length;
            Console.WriteLine($"  Saved {savedCount:N0} / {productsToAdd.Count:N0} rows...");
        }

        // ── Fix 2: Publish song song theo batch thay vì await từng message ──
        // Vấn đề cũ: foreach + await Publish() → serial, mỗi lần phải chờ
        // broker ACK → tắc nghẽn tại ~500 msg/s do network round-trip.
        //
        // Giải pháp: chia message thành batches 500, publish mỗi batch song song
        // bằng Task.WhenAll, chạy tối đa 10 batches đồng thời.
        // → Gộp nhiều publish channel trên cùng 1 connection TCP → throughput
        //   tăng lên 5.000-15.000 msg/s tùy latency mạng.
        const int publishBatchSize = 500;
        const int maxParallelBatches = 10;
        int publishedCount = 0;
        Console.WriteLine("Publishing product creation events to EventBus (parallel batches)...");

        // Tạo lookup nhanh để không dùng FirstOrDefault trong vòng lặp nóng
        var idToCategoryName = categoryMap.ToDictionary(kv => kv.Value, kv => kv.Key);

        await Parallel.ForEachAsync(
            productsToAdd.Chunk(publishBatchSize),
            new ParallelOptions { MaxDegreeOfParallelism = maxParallelBatches },
            async (batch, ct) =>
            {
                var publishTasks = batch.Select(product =>
                {
                    var categoryName = idToCategoryName.GetValueOrDefault(product.CategoryId, "Unknown");

                    var eventMsg = new ProductCreatedEvent(
                        product.Id,
                        product.Name,
                        product.Description ?? "",
                        product.Price,
                        categoryName,
                        product.IsActive,
                        product.ImageUrl,
                        product.CategoryId,
                        product.StockQuantity
                    );

                    return publishEndpoint.Publish(eventMsg, ct);
                });

                await Task.WhenAll(publishTasks);

                var count = Interlocked.Add(ref publishedCount, batch.Length);
                Console.WriteLine($"  Published {count:N0} / {productsToAdd.Count:N0} events...");
            });

        Console.WriteLine($"Successfully seeded {productsToAdd.Count} products and queued integration events.");
    }

    private static async Task<Dictionary<string, int>> SeedCategoriesAsync(ProductDbContext dbContext)
    {
        var categoryDefinitions = new Dictionary<string, string>
        {
            ["Áo"] = "Áo thun, polo, sơ mi và lớp mặc ngoài nhẹ cho ngày thường.",
            ["Áo khoác"] = "Denim jacket, bomber, windbreaker và hoodie thời trang.",
            ["Quần"] = "Jeans, chino, cargo, short và jogger dễ phối đồ.",
            ["Giày"] = "Sneaker, giày chạy bộ, canvas, boot và sandal.",
            ["Túi & phụ kiện"] = "Balo, túi tote, ví, nón, thắt lưng, đồng hồ và phụ kiện thời trang.",
            ["Đồ thể thao"] = "Trang phục và phụ kiện tập luyện, chạy bộ và di chuyển ngoài trời.",
            ["Đồ lót"] = "Trang phục lót thoải mái, co giãn tốt và thoáng khí.",
            ["Đồ bộ"] = "Đồ mặc nhà mềm mại, thoải mái và phong cách."
        };

        var categoryNames = categoryDefinitions.Keys.ToList();
        var existingCategories = await dbContext.Categories
            .Where(c => categoryNames.Contains(c.Name))
            .ToListAsync();

        var existingNamesSet = new HashSet<string>(existingCategories.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        var toAdd = categoryDefinitions
            .Where(d => !existingNamesSet.Contains(d.Key))
            .Select(d => new Category { Name = d.Key, Description = d.Value })
            .ToList();

        if (toAdd.Count > 0)
        {
            dbContext.Categories.AddRange(toAdd);
            await dbContext.SaveChangesAsync();
        }

        return await dbContext.Categories
            .Where(c => categoryNames.Contains(c.Name))
            .ToDictionaryAsync(c => c.Name, c => c.Id);
    }

    private static decimal GetLogicalPrice(string subcategory, Random random)
    {
        if (subcategory.Contains("thun") || subcategory.Contains("tank top"))
            return random.Next(15, 30) * 10000; // 150k - 300k
        if (subcategory.Contains("polo") || subcategory.Contains("sơ mi") || subcategory.Contains("sweater") || subcategory.Contains("len"))
            return random.Next(28, 48) * 10000; // 280k - 480k
        if (subcategory.Contains("khoác") || subcategory.Contains("hoodie") || subcategory.Contains("blazer") || subcategory.Contains("varsity"))
            return random.Next(55, 129) * 10000; // 550k - 1.29M
        if (subcategory.Contains("jeans") || subcategory.Contains("kaki") || subcategory.Contains("tây"))
            return random.Next(45, 75) * 10000; // 450k - 750k
        if (subcategory.Contains("short") || subcategory.Contains("jogger"))
            return random.Next(22, 39) * 10000; // 220k - 390k
        if (subcategory.Contains("Giày") || subcategory.Contains("boot") || subcategory.Contains("loafer"))
            return random.Next(69, 159) * 10000; // 690k - 1.59M
        if (subcategory.Contains("Balo") || subcategory.Contains("Đồng hồ"))
            return random.Next(50, 180) * 10000; // 500k - 1.8M
        if (subcategory.Contains("Ví") || subcategory.Contains("Thắt lưng") || subcategory.Contains("kính") || subcategory.Contains("Nón"))
            return random.Next(12, 39) * 10000; // 120k - 390k
        if (subcategory.Contains("Vớ") || subcategory.Contains("Dép"))
            return random.Next(4, 15) * 10000; // 40k - 150k
        if (subcategory.Contains("Đồ thể thao") || subcategory.Contains("quickdry") || subcategory.Contains("legging"))
            return random.Next(25, 59) * 10000; // 250k - 590k
        if (subcategory.Contains("lót") || subcategory.Contains("ngực"))
            return random.Next(5, 25) * 10000; // 50k - 250k

        return random.Next(15, 60) * 10000; // Default 150k - 600k
    }

    private static string GenerateProductName(
        string brand,
        string subcategory,
        string material,
        string fit,
        string color,
        string sku)
        => $"{brand} {subcategory} {material} {fit} màu {color} ({sku})";

    private static string GenerateDescription(
        Faker faker,
        string name,
        string subcat,
        string material,
        string fit,
        string color,
        string collection)
    {
        var occasion = faker.PickRandom("đi làm", "đi học", "dạo phố", "du lịch", "tập luyện nhẹ", "mặc nhà", "cuối tuần");
        var care = faker.PickRandom("ít nhăn", "dễ giặt", "giữ form tốt", "thoáng khí", "mềm da", "dễ phối layer");
        var detail = faker.PickRandom("đường may gọn", "bề mặt vải mịn", "phom dáng cân đối", "màu sắc dễ phối", "cảm giác mặc nhẹ");

        var templates = new[]
        {
            $"{name} thuộc bộ sưu tập {collection}, dùng chất liệu {material} với phom {fit}. Tone {color} dễ phối cho lịch trình {occasion}, nổi bật nhờ {detail}.",
            $"{subcat} màu {color} được hoàn thiện theo hướng {care}, phù hợp để mặc {occasion}. Chất liệu {material} tạo cảm giác thoải mái mà vẫn giữ vẻ chỉn chu.",
            $"Thiết kế {fit} trong dòng {collection} giúp {name} cân bằng giữa tiện dụng và phong cách. Điểm cộng là {detail}, chất vải {material} và sắc {color} hiện đại.",
            $"{name} là lựa chọn gọn gàng cho tủ đồ hằng ngày: {care}, {detail}, dễ kết hợp với sneaker, túi tote hoặc các lớp áo khoác nhẹ."
        };

        return faker.PickRandom(templates);
    }
}
