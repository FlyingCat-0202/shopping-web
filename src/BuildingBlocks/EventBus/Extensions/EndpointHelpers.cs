using System;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;

public static class EndpointHelpers
{
    public static readonly AuthorizeAttribute AdminOnly = new() { Roles = "Admin" };

    public static bool TryGetCustomerId(ClaimsPrincipal user, out Guid customerId)
    {
        var userIdString = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdString, out customerId);
    }

    public static string NormalizeVietnamese(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

        for (int i = 0; i < normalizedString.Length; i++)
        {
            char c = normalizedString[i];
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}