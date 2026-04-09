using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace EFPerfDemo.Data;

// ─── DbContext ────────────────────────────────────────────────────────────

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Models.Customer> Customers => Set<Models.Customer>();
    public DbSet<Models.Order>    Orders    => Set<Models.Order>();
    public DbSet<Models.OrderItem> OrderItems => Set<Models.OrderItem>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Models.Customer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.City).HasMaxLength(100);
            e.HasIndex(x => x.Email);          // indexed
        });

        mb.Entity<Models.Order>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Total).HasColumnType("decimal(18,2)");
            e.Property(x => x.Status).HasMaxLength(50);
            e.HasOne(x => x.Customer)
             .WithMany(c => c.Orders)
             .HasForeignKey(x => x.CustomerId);
            // NOTE: no index on CustomerId — intentional for "Missing Index" demo
        });

        mb.Entity<Models.OrderItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ProductName).HasMaxLength(200);
            e.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.Order)
             .WithMany(o => o.Items)
             .HasForeignKey(x => x.OrderId);
        });
    }
}

// ─── Query Counter Interceptor ────────────────────────────────────────────

public class QueryCountInterceptor : DbCommandInterceptor
{
    private int _count;
    private readonly List<string> _sqls = [];

    public int Count => _count;
    public IReadOnlyList<string> Sqls => _sqls;

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _count);
        _sqls.Add(command.CommandText.Trim());
        return base.ReaderExecutedAsync(command, eventData, result, ct);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        Interlocked.Increment(ref _count);
        _sqls.Add(command.CommandText.Trim());
        return base.ReaderExecuted(command, eventData, result);
    }

    public void Reset() { _count = 0; _sqls.Clear(); }
}
