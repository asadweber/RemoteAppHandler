using Microsoft.AspNetCore.Authorization;

namespace HandelConsoleApp.Web.Authorization;

public sealed class AdGroupRequirement(string[] allowedGroups) : IAuthorizationRequirement
{
    public IReadOnlyList<string> AllowedGroups { get; } = allowedGroups;
}
