namespace EFPerfDemo.Models;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string City { get; set; } = "";
    public ICollection<Order> Orders { get; set; } = [];
}

public class Order
{
    public int Id { get; set; }
    public decimal Total { get; set; }
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = "";

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public ICollection<OrderItem> Items { get; set; } = [];
}

public class OrderItem
{
    public int Id { get; set; }
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
}

// ─── DTOs ─────────────────────────────────────────────────────────────────

public class OrderDto
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerCity { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = "";
    public int ItemCount { get; set; }
}

// ─── Benchmark Result ─────────────────────────────────────────────────────

public class BenchmarkResult
{
    public string ScenarioName { get; set; } = "";
    public string Description { get; set; } = "";
    public long ElapsedMs { get; set; }
    public int QueryCount { get; set; }
    public int RecordCount { get; set; }
    public string SqlGenerated { get; set; } = "";
    public string CodeSnippet { get; set; } = "";
    public bool IsPainPoint { get; set; }
    public string PainPointExplanation { get; set; } = "";
    public string SolutionExplanation { get; set; } = "";
    public long MemoryBytes { get; set; }
    public List<QueryStep> QuerySteps { get; set; } = new();
}

public class ComparisonViewModel
{
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public BenchmarkResult PainPoint { get; set; } = new();
    public BenchmarkResult Solution { get; set; } = new();
    public List<OrderDto> SampleData { get; set; } = [];
}
