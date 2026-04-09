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

    private (AppDbContext db, QueryCountInterceptor counter) CreateTracked()
    {
        var counter = new QueryCountInterceptor();
        var db = factory.CreateDbContext();
        // Re-add interceptor at runtime via a new options instance
        // (simplest approach: use the interceptor we registered globally)
        return (db, counter);
    }

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
            methodName,
            stopwatch.ElapsedMilliseconds,
            _counter.Count);
    }

    // ─── 1. N+1 Query Problem ─────────────────────────────────────────────

    public async Task<ComparisonViewModel> NplusOne()
    {
        _counter.Reset();
        var methodSw = Stopwatch.StartNew();

        // --- PAIN POINT ---
        var sw = Stopwatch.StartNew();
        var steps = new List<QueryStep>();
        List<OrderDto> badResult;
        long badMem = MeasureMemory(() =>
        {
            using var db = factory.CreateDbContext();
            // ── Đo Query #1 ──────────────────────────────
            var sw1 = Stopwatch.StartNew();
            var orders = db.Orders.ToList();
            sw1.Stop();

            steps.Add(new QueryStep
            {
                Label = $"Query #1 — Load all orders",
                Sql = "SELECT * FROM Orders",
                ElapsedMs = sw1.ElapsedMilliseconds,
                RowCount = orders.Count
            });

            // ── Đo Query #2…N+1 (gộp cả vòng lặp) ───────
            var sw2 = Stopwatch.StartNew();

            badResult = orders.Select(o => new OrderDto
            {
                OrderId      = o.Id,
                Total        = o.Total,
                CustomerName = db.Customers                      // Query #2…N+1
                                 .FirstOrDefault(c => c.Id == o.CustomerId)?.Name ?? "",
                OrderDate    = o.OrderDate,
                Status       = o.Status
            }).ToList();

            sw2.Stop();

            steps.Add(new QueryStep
            {
                Label = $"Query #2…{orders.Count + 1} — Load customer cho từng order ({orders.Count} queries)",
                Sql = "SELECT * FROM Customers WHERE Id = @p0  (×N lần)",
                ElapsedMs = sw2.ElapsedMilliseconds,
                RowCount = orders.Count
            });

        });
        sw.Stop();
        long badMs = sw.ElapsedMilliseconds;

        using var dbBad = factory.CreateDbContext();
        int totalOrders = dbBad.Orders.Count();

        var pain = new BenchmarkResult
        {
            ScenarioName  = "N+1 — No Include",
            Description   = "Load orders, then lazy-load Customer one-by-one inside Select()",
            ElapsedMs     = badMs,
            QueryCount    = totalOrders + 1,
            RecordCount   = totalOrders,
            MemoryBytes   = badMem,
            IsPainPoint   = true,
            SqlGenerated  = "SELECT * FROM Orders\n-- then for EACH order:\nSELECT * FROM Customers WHERE Id = @p0",
            CodeSnippet   =
@"// ❌ PAIN POINT — N+1 Queries
var orders = _db.Orders.ToList();        

return orders.Select(o => new OrderDto {
    CustomerName = o.Customer.Name         
}).ToList();",
            PainPointExplanation = $"EF fires {totalOrders + 1} separate SQL queries: 1 for orders, then 1 per order to fetch its customer. With {totalOrders} orders that's {totalOrders + 1} round-trips to the database.",
            SolutionExplanation  = "Use .Include() to tell EF to JOIN Customer in the initial query — one SQL with INNER JOIN, zero extra round-trips.",
            QuerySteps = steps
        };

        // --- SOLUTION ---
        sw.Restart();
        List<OrderDto> goodResult;
        long goodMem = MeasureMemory(() =>
        {
            using var db = factory.CreateDbContext();
            goodResult = db.Orders
                           .Include(o => o.Customer)            // Eager load
                           .AsNoTracking()
                           .Select(o => new OrderDto
                           {
                               OrderId      = o.Id,
                               Total        = o.Total,
                               CustomerName = o.Customer.Name,
                               CustomerCity = o.Customer.City,
                               OrderDate    = o.OrderDate,
                               Status       = o.Status
                           }).ToList();
        });
        sw.Stop();

        var solution = new BenchmarkResult
        {
            ScenarioName  = "Eager Loading — .Include()",
            Description   = "Use .Include(o => o.Customer) + .AsNoTracking()",
            ElapsedMs     = sw.ElapsedMilliseconds,
            QueryCount    = 1,
            RecordCount   = totalOrders,
            MemoryBytes   = goodMem,
            IsPainPoint   = false,
            SqlGenerated  =
@"SELECT o.*, c.Name, c.City
FROM   Orders o
INNER JOIN Customers c ON c.Id = o.CustomerId",
            CodeSnippet   =
@"// ✅ SOLUTION — Single query with JOIN
var orders = _db.Orders
    .Include(o => o.Customer)   // ← eager load
    .AsNoTracking()             // ← skip change tracking
    .Select(o => new OrderDto {
        CustomerName = o.Customer.Name
    }).ToList();",
            PainPointExplanation = "",
            SolutionExplanation  = ".Include() generates one INNER JOIN. AsNoTracking() skips the identity map — further reducing memory."
        };

        using var dbSample = factory.CreateDbContext();
        var sample = await dbSample.Orders
            .Include(o => o.Customer)
            .AsNoTracking()
            .Take(10)
            .Select(o => new OrderDto
            {
                OrderId      = o.Id,
                Total        = o.Total,
                CustomerName = o.Customer.Name,
                CustomerCity = o.Customer.City,
                OrderDate    = o.OrderDate,
                Status       = o.Status
            }).ToListAsync();

        var result = new ComparisonViewModel
        {
            Title      = "N+1 Query Problem",
            Category   = "Query Loading",
            PainPoint  = pain,
            Solution   = solution,
            SampleData = sample
        };

        methodSw.Stop();
        LogMethodSummary(nameof(NplusOne), methodSw);
        return result;
    }

    // ─── 2. Select * vs Projection ────────────────────────────────────────

    public async Task<ComparisonViewModel> SelectStar()
    {
        _counter.Reset();
        var methodSw = Stopwatch.StartNew();

        using var db = factory.CreateDbContext();
        // --- PAIN POINT ---
        var sw = Stopwatch.StartNew();
        long badMem = MeasureMemory(() =>
        {
            // Loads ALL columns + change-tracking overhead
            var _ = db.Orders.Include(o => o.Customer).ToList();
        });
        sw.Stop();

        using var db1 = factory.CreateDbContext();
        int total = await db1.Orders.CountAsync();

        var pain = new BenchmarkResult
        {
            ScenarioName  = "Select * — Load full entities",
            Description   = "Load entire Order + Customer entities with all columns",
            ElapsedMs     = sw.ElapsedMilliseconds,
            QueryCount    = 1,
            RecordCount   = total,
            MemoryBytes   = badMem,
            IsPainPoint   = true,
            SqlGenerated  = "SELECT o.[Id], o.[CustomerId], o.[OrderDate], o.[Status], o.[Total],\n       c.[Id], c.[Name], c.[Email], c.[City]\nFROM   Orders o\nINNER JOIN Customers c ON c.Id = o.CustomerId",
            CodeSnippet   =
@"// ❌ PAIN POINT — Loads all columns + tracks entities
var orders = _db.Orders
    .Include(o => o.Customer)
    .ToList();  // pulls City, Email, etc. even if unused

var dtos = orders.Select(o => new OrderDto {
    CustomerName = o.Customer.Name  // only needed this
}).ToList();",
            PainPointExplanation = "All columns for both tables are fetched. EF also stores every entity in its change tracker, consuming extra memory even for read-only operations.",
            SolutionExplanation  = "Project directly to DTO inside the IQueryable. EF translates the anonymous/DTO select into a lean SQL SELECT with only the needed columns."
        };

        // --- SOLUTION ---
        sw.Restart();
        long goodMem = MeasureMemory(() =>
        {
            using var db = factory.CreateDbContext();
            var _ = db.Orders
                      .AsNoTracking()
                      .Select(o => new OrderDto
                      {
                          OrderId      = o.Id,
                          Total        = o.Total,
                          CustomerName = o.Customer.Name,   // EF auto-joins
                          CustomerCity = o.Customer.City,
                          OrderDate    = o.OrderDate,
                          Status       = o.Status
                      }).ToList();
        });
        sw.Stop();

        var solution = new BenchmarkResult
        {
            ScenarioName  = "Projection — .Select() to DTO",
            Description   = "Project to DTO inside IQueryable — SQL fetches only required columns",
            ElapsedMs     = sw.ElapsedMilliseconds,
            QueryCount    = 1,
            RecordCount   = total,
            MemoryBytes   = goodMem,
            IsPainPoint   = false,
            SqlGenerated  = "SELECT o.[Id], o.[Total], o.[OrderDate], o.[Status],\n       c.[Name], c.[City]\nFROM   Orders o\nINNER JOIN Customers c ON c.Id = o.CustomerId",
            CodeSnippet   =
@"// ✅ SOLUTION — Lean SQL, no change tracker
var orders = _db.Orders
    .AsNoTracking()
    .Select(o => new OrderDto {    // ← project in IQueryable
        OrderId      = o.Id,
        Total        = o.Total,
        CustomerName = o.Customer.Name,
        OrderDate    = o.OrderDate,
        Status       = o.Status
    }).ToList();  // EF generates exact columns needed",
            SolutionExplanation  = "EF generates SELECT with only the projected columns. AsNoTracking() means no identity-map allocation. Memory usage drops significantly."
        };

        using var dbSample = factory.CreateDbContext();
        var sample = await dbSample.Orders
            .AsNoTracking()
            .Take(10)
            .Select(o => new OrderDto
            {
                OrderId      = o.Id,
                Total        = o.Total,
                CustomerName = o.Customer.Name,
                CustomerCity = o.Customer.City,
                OrderDate    = o.OrderDate,
                Status       = o.Status
            }).ToListAsync();

        var result = new ComparisonViewModel
        {
            Title      = "Select * vs Projection",
            Category   = "Data Transfer",
            PainPoint  = pain,
            Solution   = solution,
            SampleData = sample
        };

        methodSw.Stop();
        LogMethodSummary(nameof(SelectStar), methodSw);
        return result;
    }

    // ─── 3. No Pagination ─────────────────────────────────────────────────

    public async Task<ComparisonViewModel> Pagination()
    {
        _counter.Reset();
        var methodSw = Stopwatch.StartNew();

        using var db0 = factory.CreateDbContext();
        int total = await db0.Orders.CountAsync();

        // --- PAIN POINT ---
        var sw = Stopwatch.StartNew();
        long badMem = MeasureMemory(() =>
        {
            using var db = factory.CreateDbContext();
            var _ = db.Orders.AsNoTracking()
                      .Select(o => new OrderDto { OrderId = o.Id, Total = o.Total, Status = o.Status })
                      .ToList();  // ALL rows
        });
        sw.Stop();

        var pain = new BenchmarkResult
        {
            ScenarioName  = "No Pagination — Load all rows",
            Description   = "Fetch every order in one shot — no Skip/Take",
            ElapsedMs     = sw.ElapsedMilliseconds,
            QueryCount    = 1,
            RecordCount   = total,
            MemoryBytes   = badMem,
            IsPainPoint   = true,
            SqlGenerated  = "SELECT [Id], [Total], [Status]\nFROM   Orders\n-- returns ALL rows",
            CodeSnippet   =
@"// ❌ PAIN POINT — loads everything into memory
var allOrders = _db.Orders
    .AsNoTracking()
    .ToList();  // 100k rows? All in RAM.",
            PainPointExplanation = $"Retrieves all {total} orders from the database. As data grows, this causes out-of-memory errors, slow page loads, and wasted bandwidth.",
            SolutionExplanation  = "Use Skip() + Take() to fetch only the current page. Combine with a total-count query for pagination metadata."
        };

        // --- SOLUTION ---
        const int pageSize = 10, page = 1;
        sw.Restart();
        long goodMem = MeasureMemory(() =>
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

        var solution = new BenchmarkResult
        {
            ScenarioName  = "Keyset / Offset Pagination",
            Description   = $"Fetch page {page} of {pageSize} rows — Skip({(page-1)*pageSize}).Take({pageSize})",
            ElapsedMs     = sw.ElapsedMilliseconds,
            QueryCount    = 2,  // data + count
            RecordCount   = pageSize,
            MemoryBytes   = goodMem,
            IsPainPoint   = false,
            SqlGenerated  = $"SELECT [Id], [Total], [Status]\nFROM   Orders\nORDER BY [Id]\nOFFSET {(page-1)*pageSize} ROWS FETCH NEXT {pageSize} ROWS ONLY",
            CodeSnippet   =
@"// ✅ SOLUTION — paginate with Skip/Take
int page = 1, pageSize = 10;

var orders = _db.Orders
    .AsNoTracking()
    .OrderBy(o => o.Id)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .Select(o => new OrderDto { ... })
    .ToList();

int totalCount = _db.Orders.Count(); // for pager UI",
            SolutionExplanation  = $"Only {pageSize} rows transferred. SQL Server uses OFFSET/FETCH NEXT. Memory usage is constant regardless of table size."
        };

        using var dbSample = factory.CreateDbContext();
        var sample = await dbSample.Orders
            .AsNoTracking()
            .OrderBy(o => o.Id)
            .Take(10)
            .Select(o => new OrderDto
            {
                OrderId   = o.Id,
                Total     = o.Total,
                Status    = o.Status,
                OrderDate = o.OrderDate
            }).ToListAsync();

        var result = new ComparisonViewModel
        {
            Title      = "No Pagination vs Paginated Query",
            Category   = "Data Volume",
            PainPoint  = pain,
            Solution   = solution,
            SampleData = sample
        };

        methodSw.Stop();
        LogMethodSummary(nameof(Pagination), methodSw);
        return result;
    }

    // ─── 4. Tracking vs AsNoTracking ─────────────────────────────────────

    public async Task<ComparisonViewModel> Tracking()
    {
        _counter.Reset();
        var methodSw = Stopwatch.StartNew();

        using var db0 = factory.CreateDbContext();
        int total = await db0.Orders.CountAsync();

        // --- PAIN POINT ---
        var sw = Stopwatch.StartNew();
        long badMem = MeasureMemory(() =>
        {
            using var db = factory.CreateDbContext();
            var _ = db.Orders.Include(o => o.Customer).ToList();  // tracked
        });
        sw.Stop();

        var pain = new BenchmarkResult
        {
            ScenarioName  = "Tracking Queries (default)",
            Description   = "Default EF behaviour — every entity stored in ChangeTracker",
            ElapsedMs     = sw.ElapsedMilliseconds,
            QueryCount    = 1,
            RecordCount   = total,
            MemoryBytes   = badMem,
            IsPainPoint   = true,
            SqlGenerated  = "SELECT ... FROM Orders o\nINNER JOIN Customers c ON c.Id = o.CustomerId\n-- all returned entities are stored in identity map",
            CodeSnippet   =
@"// ❌ PAIN POINT — unnecessary change tracking
var orders = _db.Orders
    .Include(o => o.Customer)
    .ToList();
// EF stores ALL entities in ChangeTracker
// DetectChanges() runs on every SaveChanges
// Wasted memory for read-only screens",
            PainPointExplanation = "EF's ChangeTracker keeps a snapshot of every loaded entity. For read-only views (reports, lists) this is pure overhead — extra memory and CPU for snapshot comparison.",
            SolutionExplanation  = "Add .AsNoTracking() for any query that won't result in SaveChanges(). Or set QueryTrackingBehavior.NoTracking globally on the DbContext."
        };

        // --- SOLUTION ---
        sw.Restart();
        long goodMem = MeasureMemory(() =>
        {
            using var db = factory.CreateDbContext();
            var _ = db.Orders.Include(o => o.Customer).AsNoTracking().ToList();
        });
        sw.Stop();

        var solution = new BenchmarkResult
        {
            ScenarioName  = "AsNoTracking — read-only queries",
            Description   = "Skip ChangeTracker for queries that don't mutate data",
            ElapsedMs     = sw.ElapsedMilliseconds,
            QueryCount    = 1,
            RecordCount   = total,
            MemoryBytes   = goodMem,
            IsPainPoint   = false,
            SqlGenerated  = "SELECT ... FROM Orders o\nINNER JOIN Customers c ON c.Id = o.CustomerId\n-- entities NOT stored in identity map",
            CodeSnippet   =
@"// ✅ SOLUTION 1 — per-query
var orders = _db.Orders
    .Include(o => o.Customer)
    .AsNoTracking()   // ← skip snapshot
    .ToList();

// ✅ SOLUTION 2 — global (Program.cs)
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseQueryTrackingBehavior(
        QueryTrackingBehavior.NoTracking));",
            SolutionExplanation  = "Entities are not stored in the identity map. No snapshot is taken. SaveChanges() still works — EF will simply track those specific entities you Attach() explicitly."
        };

        using var dbSample = factory.CreateDbContext();
        var sample = await dbSample.Orders
            .Include(o => o.Customer)
            .AsNoTracking()
            .Take(10)
            .Select(o => new OrderDto
            {
                OrderId      = o.Id,
                Total        = o.Total,
                CustomerName = o.Customer.Name,
                OrderDate    = o.OrderDate,
                Status       = o.Status
            }).ToListAsync();

        var result = new ComparisonViewModel
        {
            Title      = "Tracking vs AsNoTracking",
            Category   = "Memory & CPU",
            PainPoint  = pain,
            Solution   = solution,
            SampleData = sample
        };

        methodSw.Stop();
        LogMethodSummary(nameof(Tracking), methodSw);
        return result;
    }

    // ─── 5. Cartesian Explosion ───────────────────────────────────────────

    public async Task<ComparisonViewModel> CartesianExplosion()
    {
        _counter.Reset();
        var methodSw = Stopwatch.StartNew();

        using var db0 = factory.CreateDbContext();
        int orderCount = await db0.Orders.CountAsync();
        int itemCount  = await db0.OrderItems.CountAsync();

        // --- PAIN POINT ---
        var sw = Stopwatch.StartNew();
        long badMem = MeasureMemory(() =>
        {
            using var db = factory.CreateDbContext();
            var _ = db.Orders
                      .Include(o => o.Customer)
                      .Include(o => o.Items)     // ← cartesian explosion
                      .ToList();
        });
        sw.Stop();

        int cartesianRows = orderCount * (itemCount / Math.Max(orderCount, 1));

        var pain = new BenchmarkResult
        {
            ScenarioName  = "Multiple .Include() — Cartesian Explosion",
            Description   = "Include both Customer and Items causes a huge cross-product result set",
            ElapsedMs     = sw.ElapsedMilliseconds,
            QueryCount    = 1,
            RecordCount   = orderCount,
            MemoryBytes   = badMem,
            IsPainPoint   = true,
            SqlGenerated  =
@"SELECT o.*, c.*, i.*
FROM   Orders o
INNER JOIN Customers c ON c.Id = o.CustomerId
INNER JOIN OrderItems i ON i.OrderId = o.Id
-- Result set: Orders × Items rows (cartesian product!)
-- EF duplicates Customer + Order data per item row",
            CodeSnippet   =
@"// ❌ PAIN POINT — chaining multiple collection Includes
var orders = _db.Orders
    .Include(o => o.Customer)   // ← OK
    .Include(o => o.Items)      // ← multiplies rows!
    .ToList();
// SQL returns Orders × Items rows
// EF must deduplicate in memory",
            PainPointExplanation = $"Two collection Includes produce a JOIN that multiplies result rows. With {orderCount} orders and ~{itemCount} items, SQL returns ~{itemCount} rows instead of {orderCount} — EF then deduplicates them all in memory.",
            SolutionExplanation  = "Use AsSplitQuery() to split into separate SQL statements, or load collections separately. EF Core 5+ supports this natively."
        };

        // --- SOLUTION ---
        sw.Restart();
        long goodMem = MeasureMemory(() =>
        {
            using var db = factory.CreateDbContext();
            var _ = db.Orders
                      .Include(o => o.Customer)
                      .Include(o => o.Items)
                      .AsSplitQuery()             // ← key change
                      .AsNoTracking()
                      .ToList();
        });
        sw.Stop();

        var solution = new BenchmarkResult
        {
            ScenarioName  = "AsSplitQuery() — separate statements",
            Description   = "Split multi-collection Includes into individual optimised queries",
            ElapsedMs     = sw.ElapsedMilliseconds,
            QueryCount    = 3,   // orders + customers + items
            RecordCount   = orderCount,
            MemoryBytes   = goodMem,
            IsPainPoint   = false,
            SqlGenerated  =
@"-- Query 1: orders + customers
SELECT o.*, c.*
FROM   Orders o
INNER JOIN Customers c ON c.Id = o.CustomerId

-- Query 2: items (IN list of loaded order IDs)
SELECT i.*
FROM   OrderItems i
WHERE  i.OrderId IN (1, 2, 3, ...)",
            CodeSnippet   =
@"// ✅ SOLUTION — AsSplitQuery()
var orders = _db.Orders
    .Include(o => o.Customer)
    .Include(o => o.Items)
    .AsSplitQuery()     // ← separate SQL per Include
    .AsNoTracking()
    .ToList();

// Or globally in Program.cs:
// opt.UseQuerySplittingBehavior(
//     QuerySplittingBehavior.SplitQuery);",
            SolutionExplanation  = "EF fires 3 efficient SQL statements and merges the results in memory — no cartesian product. Each query returns only its own rows."
        };

        using var dbSample = factory.CreateDbContext();
        var sample = await dbSample.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .AsSplitQuery()
            .AsNoTracking()
            .Take(10)
            .Select(o => new OrderDto
            {
                OrderId      = o.Id,
                Total        = o.Total,
                CustomerName = o.Customer.Name,
                ItemCount    = o.Items.Count,
                OrderDate    = o.OrderDate,
                Status       = o.Status
            }).ToListAsync();

        var result = new ComparisonViewModel
        {
            Title      = "Cartesian Explosion",
            Category   = "Query Shape",
            PainPoint  = pain,
            Solution   = solution,
            SampleData = sample
        };

        methodSw.Stop();
        LogMethodSummary(nameof(CartesianExplosion), methodSw);
        return result;
    }

    // ─── 6. Count() vs Any() ─────────────────────────────────────────────

    public async Task<ComparisonViewModel> CountVsAny()
    {
        _counter.Reset();
        var methodSw = Stopwatch.StartNew();

        // --- PAIN POINT ---
        var sw = Stopwatch.StartNew();
        long badMem = MeasureMemory(() =>
        {
            using var db = factory.CreateDbContext();
            var orders = db.Orders.AsNoTracking().ToList();
            bool _ = orders.Count > 0;  // loads all first!
        });
        sw.Stop();

        using var db1 = factory.CreateDbContext();
        int total = await db1.Orders.CountAsync();

        var pain = new BenchmarkResult
        {
            ScenarioName  = ".Count() > 0 or .ToList().Count",
            Description   = "Load entire result set just to check existence",
            ElapsedMs     = sw.ElapsedMilliseconds,
            QueryCount    = 1,
            RecordCount   = total,
            MemoryBytes   = badMem,
            IsPainPoint   = true,
            SqlGenerated  = "SELECT [Id], [Total], ...  FROM Orders\n-- transfers ALL rows just to check if any exist",
            CodeSnippet   =
@"// ❌ PAIN POINT — loads all rows to check existence
var orders = _db.Orders.ToList();
if (orders.Count > 0) { ... }   // ALL rows fetched!

// Also bad — SQL COUNT scans whole table
if (_db.Orders.Count() > 0) { ... }",
            PainPointExplanation = $"Loading all {total} rows to check if at least one exists is extremely wasteful. Even .Count() does a full-table scan when .Any() would stop at the first row.",
            SolutionExplanation  = ".Any() translates to SELECT TOP 1 — SQL Server stops scanning as soon as one matching row is found."
        };

        // --- SOLUTION ---
        sw.Restart();
        long goodMem = MeasureMemory(() =>
        {
            using var db = factory.CreateDbContext();
            bool _ = db.Orders.AsNoTracking().Any();
        });
        sw.Stop();

        var solution = new BenchmarkResult
        {
            ScenarioName  = ".Any() — short-circuit existence check",
            Description   = "SQL Server stops at the first matching row",
            ElapsedMs     = sw.ElapsedMilliseconds,
            QueryCount    = 1,
            RecordCount   = 1,
            MemoryBytes   = goodMem,
            IsPainPoint   = false,
            SqlGenerated  = "SELECT CASE\n  WHEN EXISTS (SELECT 1 FROM Orders)\n  THEN CAST(1 AS BIT)\n  ELSE CAST(0 AS BIT)\nEND",
            CodeSnippet   =
@"// ✅ SOLUTION — Any() = SELECT TOP 1
if (_db.Orders.Any()) { ... }

// With filter:
if (_db.Orders.Any(o => o.Status == ""Pending"")) { ... }

// SQL: SELECT CASE WHEN EXISTS(
//   SELECT 1 FROM Orders WHERE Status = 'Pending'
// ) THEN 1 ELSE 0 END",
            SolutionExplanation  = "EXISTS check returns immediately on the first hit. Zero rows transferred. Works identically for filtered checks: .Any(o => o.Status == \"Pending\")"
        };

        using var dbSample = factory.CreateDbContext();
        var sample = await dbSample.Orders
            .AsNoTracking()
            .Take(10)
            .Select(o => new OrderDto
            {
                OrderId   = o.Id,
                Total     = o.Total,
                Status    = o.Status,
                OrderDate = o.OrderDate
            }).ToListAsync();

        var result = new ComparisonViewModel
        {
            Title      = "Count() > 0  vs  Any()",
            Category   = "Existence Check",
            PainPoint  = pain,
            Solution   = solution,
            SampleData = sample
        };

        methodSw.Stop();
        LogMethodSummary(nameof(CountVsAny), methodSw);
        return result;
    }

    // ─── 7. Custom Optimized Query ─────────────────────────────────────────

    public async Task<ComparisonViewModel> CustomQuery()
    {
        _counter.Reset();
        var methodSw = Stopwatch.StartNew();
        using var db0 = factory.CreateDbContext();
        int total = await db0.Orders.CountAsync();

        // --- PAIN POINT ---
        var sw = Stopwatch.StartNew();

        string badSql = "";
        string badCode = "";

        long badMem = MeasureMemory(() =>
        {
            using var db = factory.CreateDbContext();

            var query = db.Orders
                .Include(o => o.Customer); 

            badSql = query.ToQueryString(); //  auto SQL
            badCode = query.Expression.ToString(); // auto LINQ (basic)

            var _ = query.ToList(); // execute sau cùng

            foreach (var o in _)
            {
                var customer = db.Customers
                                 .FirstOrDefault(c => c.Id == o.CustomerId);
            }
        });

        sw.Stop();
        int badQueryCount = _counter.Count;

        var pain = new BenchmarkResult
        {
            ScenarioName = "Generic Query",
            ElapsedMs = sw.ElapsedMilliseconds,
            RecordCount = total,
            QueryCount = badQueryCount,
            MemoryBytes = badMem,
            IsPainPoint = true,
            CodeSnippet = badCode,
            SqlGenerated = badSql
        };

        // --- SOLUTION ---
        _counter.Reset();

        sw.Restart();

        string goodSql = "";
        string goodCode = "";

        long goodMem = MeasureMemory(() =>
        {
            using var db = factory.CreateDbContext();

            var query = db.Orders
                .AsNoTracking()
                .Select(o => new OrderDto
                {
                    OrderId = o.Id,
                    Total = o.Total,
                    CustomerName = o.Customer.Name,
                    OrderDate = o.OrderDate,
                    Status = o.Status
                });

            goodSql = query.ToQueryString();          // auto SQL
            goodCode = query.Expression.ToString();   // auto LINQ

            var _ = query.ToList();
        });

        sw.Stop();
        int goodQueryCount = _counter.Count;


        var solution = new BenchmarkResult
        {
            ScenarioName = "Custom Optimized Query",
            ElapsedMs = sw.ElapsedMilliseconds,
            RecordCount = total,
            MemoryBytes = goodMem,
            IsPainPoint = false,
            QueryCount= goodQueryCount,
            CodeSnippet = goodCode,
            SqlGenerated = goodSql
        };

        using var dbSample = factory.CreateDbContext();
        var sample = await dbSample.Orders
            .AsNoTracking()
            .Take(10)
            .Select(o => new OrderDto
            {
                OrderId = o.Id,
                Total = o.Total,
                CustomerName = o.Customer.Name,
                OrderDate = o.OrderDate,
                Status = o.Status
            }).ToListAsync();

        var result = new ComparisonViewModel
        {
            Title = "Custom Query",
            Category = "Query Optimization",
            PainPoint = pain,
            Solution = solution,
            SampleData = sample
        };

        methodSw.Stop();
        LogMethodSummary(nameof(CustomQuery), methodSw);
        return result;
    }
}

