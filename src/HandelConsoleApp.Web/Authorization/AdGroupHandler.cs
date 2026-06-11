using Microsoft.AspNetCore.Authorization;

namespace HandelApp.Web.Authorization;

/// <summary>
/// Authorization handler that evaluates <see cref="AdGroupRequirement"/> by checking
/// whether the current user belongs to any of the requirement's allowed AD groups.
/// </summary>
/// <remarks>
/// The group-membership check is currently commented out, meaning all authenticated users
/// pass this requirement. This is intentional during development — uncomment the body and
/// enable the policies in <c>Program.cs</c> before deploying to a production environment.
/// Windows Authentication (Negotiate) must be configured for <c>IsInRole</c> to work.
/// </remarks>
public sealed class AdGroupHandler : AuthorizationHandler<AdGroupRequirement>
{
    /// <summary>
    /// Evaluates the <see cref="AdGroupRequirement"/> for the current user.
    /// </summary>
    /// <param name="context">Authorization context providing the current user's claims.</param>
    /// <param name="requirement">The requirement specifying which AD groups are allowed.</param>
    /// <returns>A completed task; the requirement outcome is set via <c>context.Succeed</c>.</returns>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdGroupRequirement requirement)
    {
        //foreach (var group in requirement.AllowedGroups)
        //{
        //    if (context.User.IsInRole(group))
        //    {
        //        context.Succeed(requirement);
        //        return Task.CompletedTask;
        //    }
        //}
        return Task.CompletedTask;
    }
}
