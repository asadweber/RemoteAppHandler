namespace HandelApp.Web.Services;

public sealed class RemoteAgentOptions
{
    public string Host                     { get; set; } = "localhost";
    public int    Port                     { get; set; } = 9876;
    public int    ConnectTimeoutSeconds    { get; set; } = 5;
    public int    CommandTimeoutSeconds    { get; set; } = 30;
    public int    ReconnectIntervalSeconds { get; set; } = 15;
}
