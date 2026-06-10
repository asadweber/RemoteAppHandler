using Microsoft.AspNetCore.Authorization;

namespace HandelApp.Web.Authorization;

public sealed class AdGroupRequirement(string[] allowedGroups) : IAuthorizationRequirement
{
    public IReadOnlyList<string> AllowedGroups { get; } = allowedGroups;
}
