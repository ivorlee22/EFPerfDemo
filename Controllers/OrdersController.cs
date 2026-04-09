using EFPerfDemo.Models;
using EFPerfDemo.Services;
using Microsoft.AspNetCore.Mvc;

namespace EFPerfDemo.Controllers;

public class OrdersController(BenchmarkService bench) : Controller
{
    public IActionResult Index() => View();

    public async Task<IActionResult> NplusOne()
    {
        var vm = await bench.NplusOne();
        return View("Benchmark", vm);
    }

    public async Task<IActionResult> SelectStar()
    {
        var vm = await bench.SelectStar();
        return View("Benchmark", vm);
    }

    public async Task<IActionResult> Pagination()
    {
        var vm = await bench.Pagination();
        return View("Benchmark", vm);
    }

    public async Task<IActionResult> Tracking()
    {
        var vm = await bench.Tracking();
        return View("Benchmark", vm);
    }

    public async Task<IActionResult> CartesianExplosion()
    {
        var vm = await bench.CartesianExplosion();
        return View("Benchmark", vm);
    }

    public async Task<IActionResult> CountVsAny()
    {
        var vm = await bench.CountVsAny();
        return View("Benchmark", vm);
    }

    public async Task<IActionResult> CustomQuery() => View("Benchmark", await bench.CustomQuery());

    public class RunRequest
    {
        public string Scenario { get; set; } = "";
    }

    [HttpPost]
    public async Task<IActionResult> Run([FromBody] RunRequest req)
    {
        var scenario = req.Scenario;

        var vm = scenario switch
        {
            "NplusOne" => await bench.NplusOne(),
            "SelectStar" => await bench.SelectStar(),
            "Pagination" => await bench.Pagination(),
            "Tracking" => await bench.Tracking(),
            "CartesianExplosion" => await bench.CartesianExplosion(),
            "CountVsAny" => await bench.CountVsAny(),
            _ => null
        };

        if (vm == null) return BadRequest();

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

    // OrdersController.cs (hoặc BenchmarkController)
    [HttpPost]
    public async Task<IActionResult> RunStep([FromBody] RunStepRequest req)
    {
        ComparisonViewModel vm = req.Scenario switch
        {
            "NplusOne" => await bench.NplusOne(),
            "SelectStar" => await bench.SelectStar(),
            "Pagination" => await bench.Pagination(),
            "Tracking" => await bench.Tracking(),
            "CartesianExplosion" => await bench.CartesianExplosion(),
            "CountVsAny" => await   bench.CountVsAny(),
            "CustomQuery" => await bench.CustomQuery(),
            _ => throw new ArgumentException("Unknown scenario")
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

    public record RunStepRequest(string Scenario, string Step);
}
