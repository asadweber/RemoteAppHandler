using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using HandelApp.Web.Models;

namespace HandelApp.Web.Controllers;

/// <summary>
/// Standard ASP.NET Core MVC controller for application-level pages (home, privacy, error).
/// Not involved in agent communication.
/// </summary>
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    /// <summary>
    /// Initialises the controller with a logger injected by the DI container.
    /// </summary>
    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    /// <summary>Renders the application home page.</summary>
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>Renders the privacy policy page.</summary>
    public IActionResult Privacy()
    {
        return View();
    }

    /// <summary>
    /// Renders the error page. Response caching is disabled so error pages are never
    /// served stale from a proxy or browser cache.
    /// </summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
