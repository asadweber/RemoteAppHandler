namespace HandelApp.Agent.Services;

public sealed class AgentOptions
{
    public int      ListenPort               { get; set; } = 9876;
    public string   BindAddress              { get; set; } = "127.0.0.1";
    public string[] AllowedClientIps         { get; set; } = [];
    public int      MaxConcurrentConnections { get; set; } = 5;
}
