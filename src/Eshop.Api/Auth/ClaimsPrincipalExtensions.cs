using System.Globalization;
using System.Security.Claims;

namespace Eshop.Api.Auth;

/// <summary>Reads the signed-in party from a token. Inject <see cref="ClaimsPrincipal"/> into a handler to use these.</summary>
internal static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in Customer's id, or null when the token is not a Customer token.</summary>
    public static int? CustomerId(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Customer)
        && int.TryParse(user.FindFirstValue(EshopClaims.Subject), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    /// <summary>The signed-in Customer's id; only call on endpoints that require <see cref="Roles.Customer"/>.</summary>
    public static int RequiredCustomerId(this ClaimsPrincipal user) =>
        user.CustomerId() ?? throw new InvalidOperationException("Not a Customer token.");

    /// <summary>The signed-in Staff member's username, or null when the token is not a Staff token.</summary>
    public static string? StaffUsername(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Staff) ? user.FindFirstValue(EshopClaims.Username) : null;

    /// <summary>The signed-in Staff member's username; only call on endpoints that require <see cref="Roles.Staff"/>.</summary>
    public static string RequiredStaffUsername(this ClaimsPrincipal user) =>
        user.StaffUsername() ?? throw new InvalidOperationException("Not a Staff token.");
}
