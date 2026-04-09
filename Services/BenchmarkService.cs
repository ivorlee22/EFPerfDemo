using System.Diagnostics;
using EFPerfDemo.Data;
using EFPerfDemo.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EFPerfDemo.Services;

public class BenchmarkService(
    IDbContextFactory<AppDbContext> factory,
    QueryCountInterceptor counter,
    ILogger<BenchmarkService> logger)
{
    private readonly QueryCountInterceptor _counter = counter;
    private readonly ILogger<BenchmarkService> _logger = logger;

    // ─── Helper ──────────────────────────────────────────────────────────
    private static long MeasureMemory(Action action)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetTotalMemory(true);
        action();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        return Math.Max(0, GC.GetTotalMemory(false) - before);
    }

    private void LogMethodSummary(string methodName, Stopwatch stopwatch)
    {
        _logger.LogInformation(
            "Benchmark method {MethodName} completed in {ElapsedMs} ms with {QueryCount} total queries.",
            methodName, stopwatch.ElapsedMilliseconds, _counter.Count);
    }

    // ─── 1. N+1 Query Problem ─────────────────────────────────────────────
    public async Task<ComparisonViewModel> NplusOne(bool skipBenchmark = false, bool onlyPain = false, bool onlySolution = false)
    {

        var methodSw = Stopwatch.StartNew();

        long badMs = 0, badMem = 0, goodMs = 0, goodMem = 0;
        var steps = new List<QueryStep>();
        int totalOrders = 0;

        using var dbCount = factory.CreateDbContext();
        totalOrders = await dbCount.Orders.CountAsync();
        _counter.Reset();

        if (!skipBenchmark)
        {
            // === PAIN POINT ===
            if (!onlySolution)
            {
                var sw = Stopwatch.StartNew();
                badMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    var sw1 = Stopwatch.StartNew();
                    var orders = db.Orders.ToList();
                    sw1.Stop();

                    steps.Add(new QueryStep
                    {
                        Label = "Query #1 — Load all orders",
                        Sql = "SELECT * FROM Orders",
                        ElapsedMs = sw1.ElapsedMilliseconds,
                        RowCount = orders.Count
                    });

                    var sw2 = Stopwatch.StartNew();
                    var badResult = orders.Select(o => new OrderDto
                    {
                        OrderId = o.Id,
                        Total = o.Total,
                        CustomerName = db.Customers.FirstOrDefault(c => c.Id == o.CustomerId)?.Name ?? "",
                        OrderDate = o.OrderDate,
                        Status = o.Status
                    }).ToList();
                    sw2.Stop();

                    steps.Add(new QueryStep
                    {
                        Label = $"Query #2…{orders.Count + 1} — Load customer per order ({orders.Count} queries)",
                        Sql = "SELECT * FROM Customers WHERE Id = @p0 (×N times)",
                        ElapsedMs = sw2.ElapsedMilliseconds,
                        RowCount = orders.Count
                    });
                });
                sw.Stop();
                badMs = sw.ElapsedMilliseconds;
            }

            // === SOLUTION ===
            if (!onlyPain)
            {
                var sw = Stopwatch.StartNew();
                goodMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    var _ = db.Orders
                        .Include(o => o.Customer)
                        .AsNoTracking()
                        .Select(o => new OrderDto
                        {
                            OrderId = o.Id,
                            Total = o.Total,
                            CustomerName = o.Customer.Name,
                            OrderDate = o.OrderDate,
                            Status = o.Status
                        }).ToList();
                });
                sw.Stop();
                goodMs = sw.ElapsedMilliseconds;
            }
        }

        var pain = new BenchmarkResult
        {
            ScenarioName = "N+1 — No Include",
            ElapsedMs = badMs,
            QueryCount = onlySolution ? 0 : totalOrders + 1,
            RecordCount = totalOrders,
            MemoryBytes = badMem,
            IsPainPoint = true,
            SqlGenerated = "SELECT * FROM Orders\n-- then for EACH order:\nSELECT * FROM Customers WHERE Id = @p0",
            CodeSnippet = @"
var orders = _db.Orders.ToList();
var dtos = orders.Select(o => new OrderDto 
{ 
    CustomerName = db.Customers.FirstOrDefault(c => c.Id == o.CustomerId)?.Name 
}).ToList();",
            PainPointExplanation = $"EF fires {totalOrders + 1} separate SQL queries: 1 for orders + 1 per order for customer.",
            SolutionExplanation = "Use .Include() to eager load with a single JOIN query.",
            QuerySteps = steps
        };

        var solution = new BenchmarkResult
        {
            ScenarioName = "Eager Loading — .Include()",
            ElapsedMs = goodMs,
            QueryCount = onlyPain ? 0 : 1,
            RecordCount = totalOrders,
            MemoryBytes = goodMem,
            IsPainPoint = false,
            SqlGenerated = @"SELECT o.*, c.Name FROM Orders o 
LEFT JOIN Customers c ON c.Id = o.CustomerId",
            CodeSnippet = @"
var orders = _db.Orders
    .Include(o => o.Customer)
    .AsNoTracking()
    .Select(o => new OrderDto 
    { 
        CustomerName = o.Customer.Name 
    }).ToList();",
            SolutionExplanation = ".Include() + AsNoTracking() generates one efficient JOIN and skips change tracking."
        };

        methodSw.Stop();
        LogMethodSummary(nameof(NplusOne), methodSw);

        return new ComparisonViewModel
        {
            Title = "N+1 Query Problem",
            Category = "Query Loading",
            PainPoint = pain,
            Solution = solution,
        };
    }

    // ─── 2. Select * vs Projection ────────────────────────────────────────
    public async Task<ComparisonViewModel> SelectStar(bool skipBenchmark = false, bool onlyPain = false, bool onlySolution = false)
    {
        var methodSw = Stopwatch.StartNew();

        long badMs = 0, badMem = 0, goodMs = 0, goodMem = 0;
        using var db1 = factory.CreateDbContext();
        int total = await db1.Orders.CountAsync();
        _counter.Reset();


        if (!skipBenchmark)
        {
            if (!onlySolution)
            {
                var sw = Stopwatch.StartNew();
                badMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    var _ = db.Orders.ToList();
                });
                sw.Stop();
                badMs = sw.ElapsedMilliseconds;
            }

            if (!onlyPain)
            {
                var sw = Stopwatch.StartNew();
                goodMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    var _ = db.Orders
                        .AsNoTracking()
                        .Select(o => new OrderDto
                        {
                            OrderId = o.Id,
                            Total = o.Total,
                        }).ToList();
                });
                sw.Stop();
                goodMs = sw.ElapsedMilliseconds;
            }
        }

        var pain = new BenchmarkResult
        {
            ScenarioName = "Select * — Load full entities",
            ElapsedMs = badMs,
            QueryCount = onlySolution ? 0 : 1,
            RecordCount = total,
            MemoryBytes = badMem,
            IsPainPoint = true,
            SqlGenerated = "SELECT o.*, c.* FROM Orders o LEFT JOIN Customers c ...",
            CodeSnippet = @"
var orders = _db.Orders.Include(o => o.Customer).ToList();",
            PainPointExplanation = "Loads all columns from both tables + change tracking overhead.",
            SolutionExplanation = "Use .Select() projection to fetch only needed columns."
        };

        var solution = new BenchmarkResult
        {
            ScenarioName = "Projection — .Select() to DTO",
            ElapsedMs = goodMs,
            QueryCount = onlyPain ? 0 : 1,
            RecordCount = total,
            MemoryBytes = goodMem,
            IsPainPoint = false,
            SqlGenerated = "SELECT only required columns ...",
            CodeSnippet = @"
var orders = _db.Orders
    .AsNoTracking()
    .Select(o => new OrderDto { 
          OrderId = o.Id,
          Total = o.Total,
    })
    .ToList();",
            SolutionExplanation = "EF translates projection into lean SQL with only needed columns + no tracking."
        };

        using var dbSample = factory.CreateDbContext();

        methodSw.Stop();
        LogMethodSummary(nameof(SelectStar), methodSw);

        return new ComparisonViewModel
        {
            Title = "Select * vs Projection",
            Category = "Data Transfer",
            PainPoint = pain,
            Solution = solution,
        };
    }

    // ─── 3. No Pagination ─────────────────────────────────────────────────
    public async Task<ComparisonViewModel> Pagination(bool skipBenchmark = false, bool onlyPain = false, bool onlySolution = false)
    {
        var methodSw = Stopwatch.StartNew();

        using var db0 = factory.CreateDbContext();
        int total = await db0.Orders.CountAsync();
        _counter.Reset();


        long badMs = 0, badMem = 0, goodMs = 0, goodMem = 0;

        if (!skipBenchmark)
        {
            if (!onlySolution)
            {
                var sw = Stopwatch.StartNew();
                badMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    var _ = db.Orders
                        .ToList();
                });
                sw.Stop();
                badMs = sw.ElapsedMilliseconds;
            }

            if (!onlyPain)
            {
                const int pageSize = 10, page = 1;
                var sw = Stopwatch.StartNew();
                goodMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    var _ = db.Orders.AsNoTracking()
                        .OrderBy(o => o.Id)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .Select(o => new OrderDto { OrderId = o.Id, Total = o.Total, Status = o.Status })
                        .ToList();
                });
                sw.Stop();
                goodMs = sw.ElapsedMilliseconds;
            }
        }

        var pain = new BenchmarkResult
        {
            ScenarioName = "No Pagination — Load all rows",
            ElapsedMs = badMs,
            QueryCount = onlySolution ? 0 : 1,
            RecordCount = total,
            MemoryBytes = badMem,
            IsPainPoint = true,
            SqlGenerated = "SELECT * FROM Orders -- returns ALL rows",
            CodeSnippet = @"
var allOrders = _db.Orders.AsNoTracking().ToList();",
            PainPointExplanation = $"Loads all {total} records into memory — dangerous when table grows.",
            SolutionExplanation = "Use Skip() + Take() for true pagination."
        };

        var solution = new BenchmarkResult
        {
            ScenarioName = "Keyset / Offset Pagination",
            ElapsedMs = goodMs,
            QueryCount = onlyPain ? 0 : 2,
            RecordCount = 10,
            MemoryBytes = goodMem,
            IsPainPoint = false,
            SqlGenerated = "SELECT ... OFFSET ... FETCH NEXT 10 ROWS ONLY",
            CodeSnippet = @"
var orders = _db.Orders
    .AsNoTracking()
    .OrderBy(o => o.Id)
    .Skip((page-1)*pageSize)
    .Take(pageSize)
    .ToList();",
            SolutionExplanation = "Only loads current page. Memory usage stays constant."
        };

        using var dbSample = factory.CreateDbContext();

        methodSw.Stop();
        LogMethodSummary(nameof(Pagination), methodSw);

        return new ComparisonViewModel
        {
            Title = "No Pagination vs Paginated Query",
            Category = "Data Volume",
            PainPoint = pain,
            Solution = solution,
        };
    }

    // ─── 4. Tracking vs AsNoTracking ─────────────────────────────────────
    public async Task<ComparisonViewModel> Tracking(bool skipBenchmark = false, bool onlyPain = false, bool onlySolution = false)
    {
        var methodSw = Stopwatch.StartNew();

        using var db0 = factory.CreateDbContext();
        int total = await db0.Orders.CountAsync();
        _counter.Reset();


        long badMs = 0, badMem = 0, goodMs = 0, goodMem = 0;

        if (!skipBenchmark)
        {
            if (!onlySolution)
            {
                var sw = Stopwatch.StartNew();
                badMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    var _ = db.Orders.Include(o => o.Customer).ToList();
                });
                sw.Stop();
                badMs = sw.ElapsedMilliseconds;
            }

            if (!onlyPain)
            {
                var sw = Stopwatch.StartNew();
                goodMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    var _ = db.Orders.Include(o => o.Customer).AsNoTracking().ToList();
                });
                sw.Stop();
                goodMs = sw.ElapsedMilliseconds;
            }
        }

        var pain = new BenchmarkResult
        {
            ScenarioName = "Tracking Queries (default)",
            ElapsedMs = badMs,
            QueryCount = onlySolution ? 0 : 1,
            RecordCount = total,
            MemoryBytes = badMem,
            IsPainPoint = true,
            SqlGenerated = "SELECT ... -- entities stored in ChangeTracker",
            CodeSnippet = @"
var orders = _db.Orders.Include(o => o.Customer).ToList();",
            PainPointExplanation = "ChangeTracker stores snapshot of every entity → extra memory & CPU for read-only operations.",
            SolutionExplanation = "Use AsNoTracking() for read-only queries."
        };

        var solution = new BenchmarkResult
        {
            ScenarioName = "AsNoTracking — read-only queries",
            ElapsedMs = goodMs,
            QueryCount = onlyPain ? 0 : 1,
            RecordCount = total,
            MemoryBytes = goodMem,
            IsPainPoint = false,
            SqlGenerated = "SELECT ... -- no tracking",
            CodeSnippet = @"
var orders = _db.Orders
    .Include(o => o.Customer)
    .AsNoTracking()
    .ToList();",
            SolutionExplanation = "No identity map, no snapshot → much lower memory usage."
        };

        using var dbSample = factory.CreateDbContext();

        methodSw.Stop();
        LogMethodSummary(nameof(Tracking), methodSw);

        return new ComparisonViewModel
        {
            Title = "Tracking vs AsNoTracking",
            Category = "Memory & CPU",
            PainPoint = pain,
            Solution = solution,
        };
    }

    // ─── 5. Cartesian Explosion ───────────────────────────────────────────
    public async Task<ComparisonViewModel> CartesianExplosion(bool skipBenchmark = false, bool onlyPain = false, bool onlySolution = false)
    {
        var methodSw = Stopwatch.StartNew();

        using var db0 = factory.CreateDbContext();
        int orderCount = await db0.Orders.CountAsync();

        _counter.Reset();

        long badMs = 0, badMem = 0, goodMs = 0, goodMem = 0;

        if (!skipBenchmark)
        {
            if (!onlySolution)
            {
                var sw = Stopwatch.StartNew();
                badMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    var _ = db.Orders
                        .Include(o => o.Customer)
                        .Include(o => o.Items)
                        .ToList();
                });
                sw.Stop();
                badMs = sw.ElapsedMilliseconds;
            }

            if (!onlyPain)
            {
                var sw = Stopwatch.StartNew();
                goodMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    var _ = db.Orders
                        .Include(o => o.Customer)
                        .Include(o => o.Items)
                        .AsSplitQuery()
                        .AsNoTracking()
                        .ToList();
                });
                sw.Stop();
                goodMs = sw.ElapsedMilliseconds;
            }
        }

        var pain = new BenchmarkResult
        {
            ScenarioName = "Multiple .Include() — Cartesian Explosion",
            ElapsedMs = badMs,
            QueryCount = onlySolution ? 0 : 1,
            RecordCount = orderCount,
            MemoryBytes = badMem,
            IsPainPoint = true,
            SqlGenerated = @"SELECT o.*, c.*, i.* FROM Orders o 
LEFT JOIN ... -- Cartesian product!",
            CodeSnippet = @"
var orders = _db.Orders
    .Include(o => o.Customer)
    .Include(o => o.Items)
    .ToList();",
            PainPointExplanation = "Multiple collection Includes cause row multiplication in SQL. EF must deduplicate in memory.",
            SolutionExplanation = "Use .AsSplitQuery() to split into separate efficient queries."
        };

        var solution = new BenchmarkResult
        {
            ScenarioName = "AsSplitQuery() — separate statements",
            ElapsedMs = goodMs,
            QueryCount = onlyPain ? 0 : 3,
            RecordCount = orderCount,
            MemoryBytes = goodMem,
            IsPainPoint = false,
            SqlGenerated = @"-- Query 1: orders + customer
-- Query 2: items (IN clause)",
            CodeSnippet = @"
    .Include(o => o.Customer)
    .Include(o => o.Items)
    .AsSplitQuery()
    .AsNoTracking()
    .ToList();",
            SolutionExplanation = "EF executes separate SQL statements → no cartesian product."
        };

        using var dbSample = factory.CreateDbContext();

        methodSw.Stop();
        LogMethodSummary(nameof(CartesianExplosion), methodSw);

        return new ComparisonViewModel
        {
            Title = "Cartesian Explosion",
            Category = "Query Shape",
            PainPoint = pain,
            Solution = solution,
        };
    }

    // ─── 6. Count() vs Any() ─────────────────────────────────────────────
    public async Task<ComparisonViewModel> CountVsAny(bool skipBenchmark = false, bool onlyPain = false, bool onlySolution = false)
    {
        var methodSw = Stopwatch.StartNew();

        using var db1 = factory.CreateDbContext();
        int total = await db1.Orders.CountAsync();
        _counter.Reset();


        long badMs = 0, badMem = 0, goodMs = 0, goodMem = 0;

        if (!skipBenchmark)
        {
            if (!onlySolution)
            {
                var sw = Stopwatch.StartNew();
                badMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    var orders = db.Orders.AsNoTracking().ToList();
                    bool _ = orders.Count > 0;
                });
                sw.Stop();
                badMs = sw.ElapsedMilliseconds;
            }

            if (!onlyPain)
            {
                var sw = Stopwatch.StartNew();
                goodMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    bool _ = db.Orders.AsNoTracking().Any();
                });
                sw.Stop();
                goodMs = sw.ElapsedMilliseconds;
            }
        }

        var pain = new BenchmarkResult
        {
            ScenarioName = ".Count() > 0 or .ToList().Count",
            ElapsedMs = badMs,
            QueryCount = onlySolution ? 0 : 1,
            RecordCount = total,
            MemoryBytes = badMem,
            IsPainPoint = true,
            SqlGenerated = "SELECT all rows just to check Count",
            CodeSnippet = @"
var orders = _db.Orders.ToList();
if (orders.Count > 0) ...",
            PainPointExplanation = "Loads all rows (or full scan) just to check existence.",
            SolutionExplanation = ".Any() uses EXISTS / TOP 1 and stops immediately."
        };

        var solution = new BenchmarkResult
        {
            ScenarioName = ".Any() — short-circuit existence check",
            ElapsedMs = goodMs,
            QueryCount = onlyPain ? 0 : 1,
            RecordCount = 1,
            MemoryBytes = goodMem,
            IsPainPoint = false,
            SqlGenerated = "SELECT CASE WHEN EXISTS (...)",
            CodeSnippet = @"
if (_db.Orders.Any()) { ... }",
            SolutionExplanation = "SQL stops at the first matching row. Very fast."
        };

        //using var dbSample = factory.CreateDbContext();

        methodSw.Stop();
        LogMethodSummary(nameof(CountVsAny), methodSw);

        return new ComparisonViewModel
        {
            Title = "Count() > 0 vs Any()",
            Category = "Existence Check",
            PainPoint = pain,
            Solution = solution,
        };
    }

    // ─── 7. Custom Optimized Query ─────────────────────────────────────────
    public async Task<ComparisonViewModel> CustomQuery(bool skipBenchmark = false, bool onlyPain = false, bool onlySolution = false)
    {
        _counter.Reset();
        var methodSw = Stopwatch.StartNew();

        using var db0 = factory.CreateDbContext();
        int total = await db0.Orders.CountAsync();

        long badMs = 0, badMem = 0, goodMs = 0, goodMem = 0;

        if (!skipBenchmark)
        {
            if (!onlySolution)
            {
                var sw = Stopwatch.StartNew();
                badMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    var query = db.Orders.Include(o => o.Customer);
                    var list = query.ToList();
                    foreach (var o in list)
                    {
                        var _ = db.Customers.FirstOrDefault(c => c.Id == o.CustomerId);
                    }
                });
                sw.Stop();
                badMs = sw.ElapsedMilliseconds;
            }

            if (!onlyPain)
            {
                var sw = Stopwatch.StartNew();
                goodMem = MeasureMemory(() =>
                {
                    using var db = factory.CreateDbContext();
                    var _ = db.Orders
                        .AsNoTracking()
                        .Select(o => new OrderDto
                        {
                            OrderId = o.Id,
                            Total = o.Total,
                            CustomerName = o.Customer.Name,
                            OrderDate = o.OrderDate,
                            Status = o.Status
                        }).ToList();
                });
                sw.Stop();
                goodMs = sw.ElapsedMilliseconds;
            }
        }

        var pain = new BenchmarkResult
        {
            ScenarioName = "Generic / Inefficient Query",
            ElapsedMs = badMs,
            QueryCount = onlySolution ? 0 : _counter.Count, // approx
            RecordCount = total,
            MemoryBytes = badMem,
            IsPainPoint = true,
            SqlGenerated = "Multiple queries (N+1 like)",
            CodeSnippet = "// Bad pattern with extra per-row queries"
        };

        var solution = new BenchmarkResult
        {
            ScenarioName = "Custom Optimized Projection",
            ElapsedMs = goodMs,
            QueryCount = onlyPain ? 0 : 1,
            RecordCount = total,
            MemoryBytes = goodMem,
            IsPainPoint = false,
            SqlGenerated = "Single lean projection query",
            CodeSnippet = @"// ✅ Optimized
_db.Orders
    .AsNoTracking()
    .Select(o => new OrderDto { ... })
    .ToList();"
        };

        using var dbSample = factory.CreateDbContext();

        methodSw.Stop();
        LogMethodSummary(nameof(CustomQuery), methodSw);

        return new ComparisonViewModel
        {
            Title = "Custom Query",
            Category = "Query Optimization",
            PainPoint = pain,
            Solution = solution,
        };
    }
}