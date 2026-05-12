using System.Security.Claims;

namespace Cart.API.Extensions;

internal static class EndpointHelpers
{
    internal static bool TryGetCustomerId(ClaimsPrincipal user, out Guid customerId)
    {
        var userIdString = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdString, out customerId);
    }
}
