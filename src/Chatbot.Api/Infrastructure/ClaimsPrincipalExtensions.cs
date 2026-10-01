using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Chatbot.Api.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Reads the authenticated user's id from the JWT <c>sub</c> claim.</summary>
    public static Guid GetUserId(this ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var id)
            ? id
            : throw new UnauthorizedAccessException("The access token does not identify a user.");
    }
}
