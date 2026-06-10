using HandelConsoleApp.Shared.Protocol;

namespace HandelConsoleApp.Web.Models;

public sealed class InstancesViewModel
{
    public bool               IsConnectedToAgent { get; set; }
    public List<InstanceInfo> Instances          { get; set; } = [];
    public string?            ResultMessage      { get; set; }
    public bool               IsError            { get; set; }
}
