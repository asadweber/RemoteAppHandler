using HandelApp.Shared.Protocol;

namespace HandelApp.Web.Models;

public sealed class AppsViewModel
{
    public bool                IsConnectedToAgent { get; set; }
    public List<AppDefinition> Apps               { get; set; } = [];
    public string?             ResultMessage      { get; set; }
    public bool                IsError            { get; set; }
}

public sealed class RegisterAppInputModel
{
    public string AppId               { get; set; } = string.Empty;
    public string DisplayName         { get; set; } = string.Empty;
    public string DefaultInstancePath { get; set; } = string.Empty;
    public string InstancesRootPath   { get; set; } = string.Empty;
    public string ExecutableName      { get; set; } = string.Empty;
    public string InstanceNamePrefix  { get; set; } = "Instance";
}
