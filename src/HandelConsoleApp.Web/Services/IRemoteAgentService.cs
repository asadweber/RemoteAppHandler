using HandelConsoleApp.Shared.Protocol;

namespace HandelConsoleApp.Web.Services;

public interface IRemoteAgentService
{
    bool IsConnected { get; }
    Task<AgentResponse> SendCommandAsync(AgentCommand command, CancellationToken ct = default);
}
