namespace HandelConsoleApp.Web.Models;

public sealed class ConsoleAppStatusViewModel
{
    public bool    IsConnectedToAgent { get; set; }
    public bool    IsRunning          { get; set; }
    public int?    ProcessId          { get; set; }
    public string  LastMessage        { get; set; } = string.Empty;
    public string  RequestedBy        { get; set; } = string.Empty;
    public string? ResultMessage      { get; set; }
}
