using MiniOrm.Data;
using MiniOrm.Models;

namespace MiniOrm;

public class AppDbContext : DbContext
{
    public DbSet<Product> Products { get; set; }
    public DbSet<Order> Orders { get; set; }

    public AppDbContext(string connStr) : base(connStr)
    {
        Products = new DbSet<Product>(DatabaseConnection);
        Orders = new DbSet<Order>(DatabaseConnection);
    }
}

internal static class Program
{
    private static void Main()
    {
        Console.WriteLine("Step 1: Entities are defined with [Table], [Column], [PrimaryKey]");
        Console.WriteLine("Step 2: AppDbContext inherits DbContext and registers DbSet<Product>, DbSet<Order>.");

        var connStr = Environment.GetEnvironmentVariable("MINIORM_CONN");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            throw new InvalidOperationException("MINIORM_CONN is not set.");
        }

        Console.WriteLine("Step 3: Instantiate DbContext.");
        using var db = new AppDbContext(connStr);
        Console.WriteLine("Before using DbSet, from MiniOrm.Migrations run:");
        Console.WriteLine("  dotnet run -- migrations apply");

        Console.WriteLine("Step 4: Insert entity via DbSet.");
        var keyboard = new Product
        {
            Name = "Keyboard",
            Price = 89.99m,
            Discount = null,
            InStock = true
        };

        int id = db.Products.Insert(keyboard);
        Console.WriteLine($"Inserted Product Id={id}, Discount=NULL");

        Console.WriteLine("Step 5: Query, update, delete.");
        var found = db.Products.FindById(id);
        if (found is null)
        {
            Console.WriteLine($"Product Id={id} not found.");
            return;
        }

        Console.WriteLine($"Found -> {found.Name}, Price={found.Price}, Discount={(found.Discount?.ToString() ?? "NULL")}");

        found.Price = 79.99m;
        found.Discount = 5.00m;
        db.Products.Update(found);
        Console.WriteLine($"Updated -> Price={found.Price}, Discount={found.Discount}");

        var all = db.Products.GetAll().ToList();
        Console.WriteLine($"Get all products : {all.Count}");
        db.Products.Delete(id);
        var allAfterDelete = db.Products.GetAll().ToList();
        Console.WriteLine($"Deleted Id={id} - {allAfterDelete.Count} products remaining");
    }
}
