using EFPerfDemo.Data;
using EFPerfDemo.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─── Services ─────────────────────────────────────────────────────────────

builder.Services.AddControllersWithViews();

// Use InMemory for demo (no SQL Server needed to run)
// To switch to SQL Server, replace with:
// builder.Services.AddDbContextFactory<AppDbContext>(opt =>
//     opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddDbContextFactory<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default"))
       .EnableSensitiveDataLogging()
       .LogTo(Console.WriteLine, LogLevel.Information)); 
builder.Services.AddScoped<BenchmarkService>();

// 👉 Register Query Counter
builder.Services.AddSingleton<QueryCountInterceptor>();

// 👉 DbContextFactory + SQL Server + Interceptor
builder.Services.AddDbContextFactory<AppDbContext>((sp, opt) =>
{
    var interceptor = sp.GetRequiredService<QueryCountInterceptor>();

    opt.UseSqlServer(
            builder.Configuration.GetConnectionString("Default"))
       .EnableSensitiveDataLogging()
       .LogTo(Console.WriteLine, LogLevel.Information)
       .AddInterceptors(interceptor); 
});

builder.Services.AddScoped<BenchmarkService>();

// ─── App Pipeline ─────────────────────────────────────────────────────────

var app = builder.Build();

// Seed demo data on startup
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();
    DbSeeder.Seed(db);
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Home/Error");

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
