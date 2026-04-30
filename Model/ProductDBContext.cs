using Microsoft.EntityFrameworkCore;

namespace EF;

public class ProductDBContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        
    }
}