using HandelApp.Shared.Protocol;
using HandelApp.Web.Models;
using HandelApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HandelApp.Web.Controllers;

/// <summary>
/// MVC controller for managing registered applications.
/// Provides the landing page that lists all apps and POST endpoints for registering
/// and unregistering them with the remote agent.
/// </summary>
/// <remarks>
/// This is the default controller (see <c>Program.cs</c> route configuration).
/// Like <see cref="HandelAppController"/>, all agent calls go through
/// <see cref="SendSafeAsync"/> so the list view still renders when the agent is offline.
/// </remarks>
[Authorize]
public sealed class AppsController(
    IRemoteAgentService agentService,
    ILogger<AppsController> logger) : Controller
{
    /// <summary>
    /// Displays the registered-apps list view.
    /// </summary>
    /// <param name="ct">Request cancellation token.</param>
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

    /// <summary>
    /// Registers a new application with the agent.
    /// Normalizes the <see cref="RegisterAppInputModel.AppId"/> to lowercase and trims
    /// all string fields before sending them to the agent.
    /// </summary>
    /// <param name="input">Form-bound registration data.</param>
    /// <param name="ct">Request cancellation token.</param>
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
            // Fall back to "Instance" when the user leaves the prefix blank.
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

    /// <summary>
    /// Unregistered an application from the agent. The agent will refuse if any instances
    /// are still running.
    /// </summary>
    /// <param name="appId">App identifier from the route.</param>
    /// <param name="ct">Request cancellation token.</param>
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

    /// <summary>
    /// Sends a command to the agent and swallows any exception, returning <see langword="null"/>
    /// so the calling action can still render a degraded view when the agent is offline.
    /// </summary>
    private async Task<AgentResponse?> SendSafeAsync(AgentCommand cmd, CancellationToken ct)
    {
        try { return await agentService.SendCommandAsync(cmd, ct); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not reach remote agent");
            return null;
        }
    }

    /// <summary>
    /// Writes the command result into TempData for display after the Post-Redirect-Get.
    /// </summary>
    /// <param name="response">Agent response, or <see langword="null"/> when unreachable.</param>
    /// <param name="successDefault">Fallback message for a successful response with no message text.</param>
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
