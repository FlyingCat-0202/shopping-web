using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // 1. Tìm đường dẫn đến file appsettings.json
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // 2. Lấy chuỗi kết nối (Key phải trùng với tên trong appsettings.json)
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // 3. Cấu hình EF Core
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        builder.UseNpgsql(connectionString);

        return new AppDbContext(builder.Options);
    }
}