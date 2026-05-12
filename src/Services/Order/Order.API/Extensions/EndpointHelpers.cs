using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Order.API.Extensions;

internal static class EndpointHelpers
{
    internal static readonly AuthorizeAttribute AdminOnly = new() { Roles = "Admin" };

    internal static bool TryGetCustomerId(ClaimsPrincipal user, out Guid customerId)
    {
        var userIdString = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdString, out customerId);
    }
}
