using EFPerfDemo.Models;
using EFPerfDemo.Services;
using Microsoft.AspNetCore.Mvc;

namespace EFPerfDemo.Controllers;

public class OrdersController(BenchmarkService bench) : Controller
{
    public IActionResult Index() => View();

    // ── Page-load actions — chỉ lấy metadata, không chạy benchmark ──────────
    public async Task<IActionResult> NplusOne()
        => View("Benchmark", await bench.NplusOne(skipBenchmark: true));

    public async Task<IActionResult> SelectStar()
        => View("Benchmark", await bench.SelectStar(skipBenchmark: true));

    public async Task<IActionResult> Pagination()
        => View("Benchmark", await bench.Pagination(skipBenchmark: true));

    public async Task<IActionResult> Tracking()
        => View("Benchmark", await bench.Tracking(skipBenchmark: true));

    public async Task<IActionResult> CartesianExplosion()
        => View("Benchmark", await bench.CartesianExplosion(skipBenchmark: true));

    public async Task<IActionResult> CountVsAny()
        => View("Benchmark", await bench.CountVsAny(skipBenchmark: true));

    public async Task<IActionResult> CustomQuery()
        => View("Benchmark", await bench.CustomQuery(skipBenchmark: true));

    // ── RunStep — chạy riêng Pain hoặc Solution theo yêu cầu từ JS ──────────
    public record RunStepRequest(string Scenario, string Step);

    [HttpPost]
    public async Task<IActionResult> RunStep([FromBody] RunStepRequest req)
    {
        ComparisonViewModel vm = req.Scenario switch
        {
            "NplusOne" => await bench.NplusOne(
                skipBenchmark: false,
                onlyPain: req.Step == "pain",
                onlySolution: req.Step == "solution"),

            "SelectStar" => await bench.SelectStar(
                skipBenchmark: false,
                onlyPain: req.Step == "pain",
                onlySolution: req.Step == "solution"),

            "Pagination" => await bench.Pagination(
                skipBenchmark: false,
                onlyPain: req.Step == "pain",
                onlySolution: req.Step == "solution"),

            "Tracking" => await bench.Tracking(
                skipBenchmark: false,
                onlyPain: req.Step == "pain",
                onlySolution: req.Step == "solution"),

            "CartesianExplosion" => await bench.CartesianExplosion(
                skipBenchmark: false,
                onlyPain: req.Step == "pain",
                onlySolution: req.Step == "solution"),

            "CountVsAny" => await bench.CountVsAny(
                skipBenchmark: false,
                onlyPain: req.Step == "pain",
                onlySolution: req.Step == "solution"),

            "CustomQuery" => await bench.CustomQuery(
                skipBenchmark: false,
                onlyPain: req.Step == "pain",
                onlySolution: req.Step == "solution"),

            _ => throw new ArgumentException($"Unknown scenario: {req.Scenario}")
        };

        var result = req.Step == "pain" ? vm.PainPoint : vm.Solution;

        return Ok(new
        {
            elapsedMs = result.ElapsedMs,
            queryCount = result.QueryCount,
            memoryBytes = result.MemoryBytes,
            recordCount = result.RecordCount
        });
    }

    // ── Legacy Run endpoint (giữ lại để tương thích cũ nếu cần) ──────────────
    public class RunRequest
    {
        public string Scenario { get; set; } = "";
    }

    [HttpPost]
    public async Task<IActionResult> Run([FromBody] RunRequest req)
    {
        var vm = req.Scenario switch
        {
            "NplusOne" => await bench.NplusOne(),
            "SelectStar" => await bench.SelectStar(),
            "Pagination" => await bench.Pagination(),
            "Tracking" => await bench.Tracking(),
            "CartesianExplosion" => await bench.CartesianExplosion(),
            "CountVsAny" => await bench.CountVsAny(),
            "CustomQuery" => await bench.CustomQuery(),
            _ => (ComparisonViewModel?)null
        };

        if (vm is null)
            return BadRequest("Unknown scenario");

        return Json(new
        {
            pain = new
            {
                queryCount = vm.PainPoint.QueryCount,
                elapsedMs = vm.PainPoint.ElapsedMs
            },
            solution = new
            {
                queryCount = vm.Solution.QueryCount,
                elapsedMs = vm.Solution.ElapsedMs
            }
        });
    }
}