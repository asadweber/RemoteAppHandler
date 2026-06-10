using HandelConsoleApp.Shared.Protocol;
using HandelConsoleApp.Web.Models;
using HandelConsoleApp.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HandelConsoleApp.Web.Controllers;

public sealed class ConsoleAppController(
    IRemoteAgentService agentService,
    ILogger<ConsoleAppController> logger) : Controller
{
    [HttpGet("")]
    [HttpGet("/ConsoleApp")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var response = await SendSafeAsync(new AgentCommand { Command = CommandType.ListInstances }, ct);
        var vm = new InstancesViewModel
        {
            IsConnectedToAgent = agentService.IsConnected,
            Instances          = response?.Instances ?? [],
            ResultMessage      = TempData["Result"] as string,
            IsError            = TempData["IsError"] is true
        };
        return View(vm);
    }

    [HttpPost("/ConsoleApp/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var listResponse = await SendSafeAsync(new AgentCommand { Command = CommandType.ListInstances }, ct);
        int nextNumber = 1;
        if (listResponse?.Instances is { Count: > 0 } instances)
        {
            var maxNum = instances
                .Select(i => ParseInstanceNumber(i.InstanceName))
                .Where(n => n > 0)
                .DefaultIfEmpty(0)
                .Max();
            nextNumber = maxNum + 1;
        }

        var cmd = new AgentCommand
        {
            Command        = CommandType.CreateInstance,
            InstanceNumber = nextNumber,
            RequestedBy    = User.Identity?.Name ?? "unknown"
        };
        var response = await SendSafeAsync(cmd, ct);
        SetResult(response, $"Instance-{nextNumber} created");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/ConsoleApp/Delete/{number:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int number, CancellationToken ct)
    {
        var cmd = new AgentCommand
        {
            Command        = CommandType.DeleteInstance,
            InstanceNumber = number,
            RequestedBy    = User.Identity?.Name ?? "unknown"
        };
        var response = await SendSafeAsync(cmd, ct);
        SetResult(response, $"Instance-{number} deleted");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/ConsoleApp/Start/{name}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(string name, CancellationToken ct)
    {
        var cmd = new AgentCommand
        {
            Command      = CommandType.Start,
            InstanceName = name,
            RequestedBy  = User.Identity?.Name ?? "unknown"
        };
        var response = await SendSafeAsync(cmd, ct);
        SetResult(response, $"{name} started");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/ConsoleApp/Stop/{name}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stop(string name, CancellationToken ct)
    {
        var cmd = new AgentCommand
        {
            Command      = CommandType.Stop,
            InstanceName = name,
            RequestedBy  = User.Identity?.Name ?? "unknown"
        };
        var response = await SendSafeAsync(cmd, ct);
        SetResult(response, $"{name} stopped");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/ConsoleApp/StatusJson")]
    public async Task<IActionResult> StatusJson(CancellationToken ct)
    {
        var response = await SendSafeAsync(new AgentCommand { Command = CommandType.ListInstances }, ct);
        return Json(new
        {
            connected = agentService.IsConnected,
            instances = response?.Instances ?? []
        });
    }

    private async Task<AgentResponse?> SendSafeAsync(AgentCommand cmd, CancellationToken ct)
    {
        try
        {
            return await agentService.SendCommandAsync(cmd, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not reach remote agent");
            return null;
        }
    }

    private void SetResult(AgentResponse? response, string successDefault)
    {
        if (response is null)
        {
            TempData["Result"]  = "Agent unreachable";
            TempData["IsError"] = true;
            return;
        }
        var isError = response.Status is ResponseStatus.Error or ResponseStatus.Unauthorized;
        TempData["Result"]  = string.IsNullOrEmpty(response.Message) ? successDefault : response.Message;
        TempData["IsError"] = isError;
        if (isError) logger.LogWarning("{Msg}", response.Message);
        else         logger.LogInformation("{Msg}", response.Message);
    }

    private static int ParseInstanceNumber(string name)
    {
        var parts = name.Split('-');
        return parts.Length > 0 && int.TryParse(parts[^1], out var n) ? n : 0;
    }
}
