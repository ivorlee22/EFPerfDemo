using EFPerfDemo.Models;

namespace EFPerfDemo.Data;

public static class DbSeeder
{
    private static readonly string[] Cities =
        ["Hanoi", "Ho Chi Minh City", "Da Nang", "Hue", "Can Tho", "Hai Phong","Da Lat"];

    private static readonly string[] Products =
        ["Laptop Dell XPS", "iPhone 16", "AirPods Pro", "iPad Air", "Samsung TV 55\"",
         "Keyboard Keychron", "Mouse Logitech MX", "Monitor LG 27\"", "SSD Samsung 1TB", "GPU RTX 5090"];

    private static readonly string[] Statuses =
        ["Pending", "Processing", "Shipped", "Delivered", "Cancelled"];

    public static void Seed(AppDbContext db)
    {
        if (db.Customers.Any()) return;

        var rng = new Random(42);

        var customers = Enumerable.Range(1, 5000).Select(i => new Customer
        {
            Name  = $"Customer {i:D3}",
            Email = $"customer{i}@example.com",
            City  = Cities[rng.Next(Cities.Length)]
        }).ToList();

        db.Customers.AddRange(customers);
        db.SaveChanges();

        var orders = new List<Order>();
        foreach (var c in customers)
        {
            int orderCount = rng.Next(1, 20);
            for (int j = 0; j < orderCount; j++)
            {
                var order = new Order
                {
                    CustomerId = c.Id,
                    OrderDate  = DateTime.Now.AddDays(-rng.Next(1, 365)),
                    Status     = Statuses[rng.Next(Statuses.Length)],
                    Items      = Enumerable.Range(0, rng.Next(1, 10)).Select(_ => new OrderItem
                    {
                        ProductName = Products[rng.Next(Products.Length)],
                        Quantity    = rng.Next(1, 6),
                        UnitPrice   = Math.Round((decimal)(rng.NextDouble() * 2000 + 50), 2)
                    }).ToList()
                };
                order.Total = order.Items.Sum(i => i.Quantity * i.UnitPrice);
                orders.Add(order);
            }
        }

        db.Orders.AddRange(orders);
        db.SaveChanges();
    }
}
