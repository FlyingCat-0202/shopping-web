using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace EventBus.Extensions;

public static class EndpointHelpers
{
    public static readonly AuthorizeAttribute AdminOnly = new() { Roles = "Admin" };
    public static bool TryGetCustomerId(ClaimsPrincipal user, out Guid customerId)
    {
        var userIdString = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdString, out customerId);
    }
}
