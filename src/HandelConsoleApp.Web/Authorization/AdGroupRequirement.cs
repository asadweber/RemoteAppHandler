using Microsoft.AspNetCore.Authorization;

namespace HandelApp.Web.Authorization;

/// <summary>
/// Authorization requirement that restricts access to users who belong to at least one
/// of the specified Active Directory groups.
/// </summary>
/// <remarks>
/// Evaluated by <see cref="AdGroupHandler"/>. Currently the handler body is commented out,
/// so this requirement always succeeds regardless of group membership.
/// TODO: Uncomment <see cref="AdGroupHandler.HandleRequirementAsync"/> and wire up the
/// "CanControlApp" / "CanViewStatus" policies in <c>Program.cs</c> before production use.
/// </remarks>
public sealed class AdGroupRequirement(string[] allowedGroups) : IAuthorizationRequirement
{
    /// <summary>
    /// The Active Directory group names whose members are permitted to satisfy this requirement.
    /// Populated from <c>appsettings.json</c> sections <c>Authorization:AllowedGroups</c>
    /// or <c>Authorization:ReadOnlyGroups</c>.
    /// </summary>
    public IReadOnlyList<string> AllowedGroups { get; } = allowedGroups;
}
