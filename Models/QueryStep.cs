namespace EFPerfDemo.Models
{
    public class QueryStep
    {
        public string Label { get; set; } = "";
        public string Sql { get; set; } = "";
        public long ElapsedMs { get; set; }
        public int RowCount { get; set; }
    }
}
