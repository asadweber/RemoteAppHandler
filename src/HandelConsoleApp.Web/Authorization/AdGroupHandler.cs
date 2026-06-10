using Microsoft.AspNetCore.Authorization;

namespace HandelConsoleApp.Web.Authorization;

public sealed class AdGroupHandler : AuthorizationHandler<AdGroupRequirement>
{
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
