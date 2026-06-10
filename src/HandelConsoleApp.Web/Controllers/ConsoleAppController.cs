using HandelConsoleApp.Shared.Protocol;
using HandelConsoleApp.Web.Models;
using HandelConsoleApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HandelConsoleApp.Web.Controllers;

//[Authorize]
//[Route("[controller]")]
public sealed class ConsoleAppController(
    IRemoteAgentService agentService,
    ILogger<ConsoleAppController> logger) : Controller
{
    //[Authorize(Policy = "CanViewStatus")]
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var response = await SendSafeAsync(CommandType.Status, ct);
        var vm = new ConsoleAppStatusViewModel
        {
            IsConnectedToAgent = agentService.IsConnected,
            IsRunning          = response?.IsRunning ?? false,
            ProcessId          = response?.ProcessId,
            LastMessage        = response?.Message ?? "Agent unreachable",
            RequestedBy        = User.Identity?.Name ?? "unknown",
            ResultMessage      = TempData["Result"] as string
        };
        return View(vm);
    }

    //[Authorize(Policy = "CanControlApp")]
    [HttpPost("Start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        var cmd = new AgentCommand
        {
            Command     = CommandType.Start,
            RequestedBy = User.Identity?.Name ?? "unknown"
        };
        var response = await agentService.SendCommandAsync(cmd, ct);
        logger.LogInformation("Start by {User}: {Status} - {Msg}",
            cmd.RequestedBy, response.Status, response.Message);

        TempData["Result"] = response.Message;
        return RedirectToAction(nameof(Index));
    }

    //[Authorize(Policy = "CanControlApp")]
    [HttpPost("Stop")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stop(CancellationToken ct)
    {
        var cmd = new AgentCommand
        {
            Command     = CommandType.Stop,
            RequestedBy = User.Identity?.Name ?? "unknown"
        };
        var response = await agentService.SendCommandAsync(cmd, ct);
        logger.LogInformation("Stop by {User}: {Status} - {Msg}",
            cmd.RequestedBy, response.Status, response.Message);

        TempData["Result"] = response.Message;
        return RedirectToAction(nameof(Index));
    }

    //[Authorize(Policy = "CanViewStatus")]
    [HttpGet("Status")]
    [Produces("application/json")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var response = await SendSafeAsync(CommandType.Status, ct);
        if (response is null)
            return StatusCode(503, new { error = "Agent unreachable" });
        return Ok(response);
    }

    private async Task<AgentResponse?> SendSafeAsync(CommandType type, CancellationToken ct)
    {
        try
        {
            return await agentService.SendCommandAsync(
                new AgentCommand { Command = type, RequestedBy = User.Identity?.Name ?? "unknown" }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not reach remote agent");
            return null;
        }
    }
}
