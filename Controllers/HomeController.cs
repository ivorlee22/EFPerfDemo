using Microsoft.AspNetCore.Mvc;

namespace EFPerfDemo.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
}
