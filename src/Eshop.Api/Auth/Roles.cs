namespace Eshop.Api.Auth;

/// <summary>
/// The two roles eshop-api issues tokens for. Each is also an authorization policy of the same
/// name, so an endpoint (or a whole group) is gated with <c>.RequireAuthorization(Roles.Customer)</c>
/// or <c>.RequireAuthorization(Roles.Staff)</c>. No token → 401; wrong role → 403.
/// </summary>
internal static class Roles
{
    public const string Customer = "Customer";

    public const string Staff = "Staff";
}

/// <summary>Claim types in eshop-api tokens.</summary>
internal static class EshopClaims
{
    public const string Subject = "sub";

    public const string Role = "role";

    public const string MobileNumber = "mobile_number";

    /// <summary>Staff tokens only.</summary>
    public const string Username = "username";

    /// <summary>Staff tokens only.</summary>
    public const string DisplayName = "name";
}
