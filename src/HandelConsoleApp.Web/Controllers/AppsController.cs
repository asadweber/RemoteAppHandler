using HandelApp.Shared.Protocol;
using HandelApp.Web.Models;
using HandelApp.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HandelApp.Web.Controllers;

public sealed class AppsController(
    IRemoteAgentService agentService,
    ILogger<AppsController> logger) : Controller
{    
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var response = await SendSafeAsync(new AgentCommand { Command = CommandType.ListApps }, ct);
        var vm = new AppsViewModel
        {
            IsConnectedToAgent = agentService.IsConnected,
            Apps               = response?.Apps ?? [],
            ResultMessage      = TempData["Result"] as string,
            IsError            = TempData["IsError"] is true
        };
        return View(vm);
    }

    [HttpPost("/Apps/Register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterAppInputModel input, CancellationToken ct)
    {
        var def = new AppDefinition
        {
            AppId               = input.AppId.Trim().ToLowerInvariant(),
            DisplayName         = input.DisplayName.Trim(),
            DefaultInstancePath = input.DefaultInstancePath.Trim(),
            InstancesRootPath   = input.InstancesRootPath.Trim(),
            ExecutableName      = input.ExecutableName.Trim(),
            InstanceNamePrefix  = string.IsNullOrWhiteSpace(input.InstanceNamePrefix) ? "Instance" : input.InstanceNamePrefix.Trim()
        };

        var cmd = new AgentCommand
        {
            Command       = CommandType.RegisterApp,
            AppDefinition = def,
            RequestedBy   = User.Identity?.Name ?? "unknown"
        };
        var response = await SendSafeAsync(cmd, ct);
        SetResult(response, $"App '{def.AppId}' registered");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/Apps/Unregister/{appId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unregister(string appId, CancellationToken ct)
    {
        var cmd = new AgentCommand
        {
            Command     = CommandType.UnregisterApp,
            AppId       = appId,
            RequestedBy = User.Identity?.Name ?? "unknown"
        };
        var response = await SendSafeAsync(cmd, ct);
        SetResult(response, $"App '{appId}' unregistered");
        return RedirectToAction(nameof(Index));
    }

    private async Task<AgentResponse?> SendSafeAsync(AgentCommand cmd, CancellationToken ct)
    {
        try { return await agentService.SendCommandAsync(cmd, ct); }
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
}
