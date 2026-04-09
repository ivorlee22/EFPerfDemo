# EF Core Performance Demo

ASP.NET Core MVC project demonstrating 6 common Entity Framework Core performance anti-patterns, with live benchmarks, SQL diffs, and code fixes.

## Requirements

- .NET 10 SDK
- Visual Studio 2022 v17.12+ (or VS Code with C# extension)

## Quick Start

```bash
git clone / unzip the project
cd EFPerfDemo
dotnet run
```

Open https://localhost:5001 in your browser.

> The project uses **InMemory database** by default — no SQL Server needed.  
> Seed data (100 customers, ~300 orders, ~700 items) is generated automatically on startup.

## Scenarios Covered

| # | Route | Anti-Pattern | Fix |
|---|-------|-------------|-----|
| 1 | `/Orders/NplusOne` | N+1 Query Problem | `.Include()` eager loading |
| 2 | `/Orders/SelectStar` | Select * full entity | `.Select()` projection to DTO |
| 3 | `/Orders/Pagination` | Load all rows | `.Skip().Take()` pagination |
| 4 | `/Orders/Tracking` | Default change tracking on reads | `.AsNoTracking()` |
| 5 | `/Orders/CartesianExplosion` | Multiple collection Includes | `.AsSplitQuery()` |
| 6 | `/Orders/CountVsAny` | `.Count() > 0` existence check | `.Any()` |

## Switch to SQL Server

In `Program.cs`, replace the InMemory registration:

```csharp
builder.Services.AddDbContextFactory<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
```

Then run:

```bash
dotnet ef migrations add Initial
dotnet ef database update
```

## Project Structure

```
EFPerfDemo/
├── Controllers/
│   ├── HomeController.cs
│   └── OrdersController.cs
├── Data/
│   ├── AppDbContext.cs        # DbContext + QueryCountInterceptor
│   └── DbSeeder.cs            # 100 customers, ~300 orders
├── Models/
│   └── Entities.cs            # Customer, Order, OrderItem, DTOs
├── Services/
│   └── BenchmarkService.cs    # All 6 benchmark scenarios
├── Views/
│   ├── Home/Index.cshtml      # Landing page
│   ├── Orders/Benchmark.cshtml # Shared comparison view
│   └── Shared/_Layout.cshtml
└── wwwroot/
    ├── css/site.css
    └── js/site.js
```
