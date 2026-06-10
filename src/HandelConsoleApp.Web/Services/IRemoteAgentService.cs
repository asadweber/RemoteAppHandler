using HandelApp.Shared.Protocol;

namespace HandelApp.Web.Services;

public interface IRemoteAgentService
{
    bool IsConnected { get; }
    Task<AgentResponse> SendCommandAsync(AgentCommand command, CancellationToken ct = default);
}
