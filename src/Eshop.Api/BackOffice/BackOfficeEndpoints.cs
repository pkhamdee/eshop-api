using Eshop.Api.Auth;

namespace Eshop.Api.BackOffice;

/// <summary>
/// The Staff back office under <c>/api/back-office</c>: catalog (Products, product photos, Product Category
/// settings), Branch management and Auctions. Every endpoint requires a Staff token (none → 401, Customer → 403).
/// </summary>
internal static class BackOfficeEndpoints
{
    public static IEndpointRouteBuilder MapBackOfficeEndpoints(this IEndpointRouteBuilder api)
    {
        var backOffice = api.MapGroup("/back-office")
            .WithTags("Back office")
            .RequireAuthorization(Roles.Staff);

        backOffice.MapProductAdministration();
        backOffice.MapProductCategoryAdministration();
        backOffice.MapBranchAdministration();
        backOffice.MapAuctionAdministration();
        backOffice.MapHandoverAdministration();
        backOffice.MapLotSaleAdministration();

        return api;
    }

    /// <summary>Field errors for a 400 ValidationProblem, filled with <see cref="Require"/>.</summary>
    internal static Dictionary<string, string[]> Errors() => new(StringComparer.Ordinal);

    internal static void Require(this Dictionary<string, string[]> errors, bool ok, string field, string message)
    {
        if (!ok)
        {
            errors[field] = [message];
        }
    }
}
