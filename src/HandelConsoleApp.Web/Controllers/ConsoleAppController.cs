using HandelApp.Shared.Protocol;
using HandelApp.Web.Models;
using HandelApp.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HandelApp.Web.Controllers;

/// <summary>
/// MVC controller for managing the instances of a specific registered application.
/// Provides views and POST endpoints for listing, creating, deleting, starting, and
/// stopping instances, plus a JSON status endpoint for polling from JavaScript.
/// </summary>
/// <remarks>
/// All mutating actions (Create, Delete, Start, Stop) use TempData to pass a result
/// message across the Post-Redirect-Get redirect, preventing duplicate submissions
/// on browser refresh.
/// <para>
/// Agent communication is wrapped by <see cref="SendSafeAsync"/>, which catches all
/// exceptions and returns <see langword="null"/> so the view still renders (with an
/// "agent unreachable" message) when the agent is offline.
/// </para>
/// </remarks>
public sealed class ConsoleAppController(
    IRemoteAgentService agentService,
    ILogger<ConsoleAppController> logger) : Controller
{
    /// <summary>
    /// Displays the instance list view for a specific app.
    /// </summary>
    /// <param name="appId">App identifier from the route.</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("/ConsoleApp/{appId}")]
    public async Task<IActionResult> Index(string appId, CancellationToken ct)
    {
        var response = await SendSafeAsync(
            new AgentCommand { Command = CommandType.ListInstances, AppId = appId }, ct);
        var vm = new InstancesViewModel
        {
            IsConnectedToAgent = agentService.IsConnected,
            Instances          = response?.Instances ?? [],
            ResultMessage      = TempData["Result"] as string,
            IsError            = TempData["IsError"] is true
        };
        ViewBag.AppId = appId;
        return View(vm);
    }

    /// <summary>
    /// Creates the next numbered instance for the app.
    /// Determines the next available number by inspecting existing instance names,
    /// then sends a <see cref="CommandType.CreateInstance"/> command to the agent.
    /// </summary>
    /// <param name="appId">App identifier from the route.</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpPost("/ConsoleApp/{appId}/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string appId, CancellationToken ct)
    {
        var listResponse = await SendSafeAsync(
            new AgentCommand { Command = CommandType.ListInstances, AppId = appId }, ct);
        int nextNumber = 1;
        if (listResponse?.Instances is { Count: > 0 } instances)
        {
            // Determine the highest existing numbered instance to avoid collisions.
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
            AppId          = appId,
            InstanceNumber = nextNumber,
            RequestedBy    = User.Identity?.Name ?? "unknown"
        };
        var response = await SendSafeAsync(cmd, ct);
        SetResult(response, $"Instance-{nextNumber} created");
        return RedirectToAction(nameof(Index), new { appId });
    }

    /// <summary>
    /// Deletes a specific numbered instance.
    /// The agent enforces that the instance must not be running before deletion.
    /// </summary>
    /// <param name="appId">App identifier from the route.</param>
    /// <param name="number">Instance number from the route.</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpPost("/ConsoleApp/{appId}/Delete/{number:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string appId, int number, CancellationToken ct)
    {
        var cmd = new AgentCommand
        {
            Command        = CommandType.DeleteInstance,
            AppId          = appId,
            InstanceNumber = number,
            RequestedBy    = User.Identity?.Name ?? "unknown"
        };
        var response = await SendSafeAsync(cmd, ct);
        SetResult(response, $"Instance-{number} deleted");
        return RedirectToAction(nameof(Index), new { appId });
    }

    /// <summary>
    /// Starts a named instance's managed process.
    /// </summary>
    /// <param name="appId">App identifier from the route.</param>
    /// <param name="name">Instance name from the route (e.g. "Default" or "Instance-2").</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpPost("/ConsoleApp/{appId}/Start/{name}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(string appId, string name, CancellationToken ct)
    {
        var cmd = new AgentCommand
        {
            Command      = CommandType.Start,
            AppId        = appId,
            InstanceName = name,
            RequestedBy  = User.Identity?.Name ?? "unknown"
        };
        var response = await SendSafeAsync(cmd, ct);
        SetResult(response, $"{name} started");
        return RedirectToAction(nameof(Index), new { appId });
    }

    /// <summary>
    /// Stops a named instance's managed process.
    /// The agent attempts a graceful shutdown before force-killing.
    /// </summary>
    /// <param name="appId">App identifier from the route.</param>
    /// <param name="name">Instance name from the route.</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpPost("/ConsoleApp/{appId}/Stop/{name}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stop(string appId, string name, CancellationToken ct)
    {
        var cmd = new AgentCommand
        {
            Command      = CommandType.Stop,
            AppId        = appId,
            InstanceName = name,
            RequestedBy  = User.Identity?.Name ?? "unknown"
        };
        var response = await SendSafeAsync(cmd, ct);
        SetResult(response, $"{name} stopped");
        return RedirectToAction(nameof(Index), new { appId });
    }

    /// <summary>
    /// Returns a JSON snapshot of the agent connection status and all instance states.
    /// Intended for JavaScript polling so the page can refresh instance run-state without
    /// a full page reload.
    /// </summary>
    /// <param name="appId">App identifier from the route.</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("/ConsoleApp/{appId}/StatusJson")]
    public async Task<IActionResult> StatusJson(string appId, CancellationToken ct)
    {
        var response = await SendSafeAsync(
            new AgentCommand { Command = CommandType.ListInstances, AppId = appId }, ct);
        return Json(new
        {
            connected = agentService.IsConnected,
            instances = response?.Instances ?? []
        });
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
    /// Uses the agent's <see cref="AgentResponse.Message"/> when non-empty, falling back
    /// to <paramref name="successDefault"/> for successful operations with no message.
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

    /// <summary>
    /// Extracts the numeric suffix from an instance name formatted as <c>Prefix-N</c>.
    /// Returns <c>0</c> for names that do not match the expected pattern (e.g. "Default").
    /// </summary>
    /// <param name="name">Instance name to parse.</param>
    private static int ParseInstanceNumber(string name)
    {
        var parts = name.Split('-');
        return parts.Length > 0 && int.TryParse(parts[^1], out var n) ? n : 0;
    }
}
