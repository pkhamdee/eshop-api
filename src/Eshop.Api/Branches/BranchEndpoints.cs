using Eshop.Data;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Api.Branches;

/// <summary>Public list of Branches still offered for Store Pickup. Staff manage them in the back office.</summary>
internal static class BranchEndpoints
{
    public static IEndpointRouteBuilder MapBranchEndpoints(this IEndpointRouteBuilder api)
    {
        var branches = api.MapGroup("/branches").WithTags("Branches");
        branches.MapGet("/", ListBranches);

        return api;
    }

    private static async Task<List<BranchResponse>> ListBranches(
        EshopDbContext db, CancellationToken cancellationToken) =>
        await db.Branches
            .Where(b => !b.IsRetired)
            .OrderBy(b => b.Id)
            .Select(b => new BranchResponse(b.Code, b.Name, b.Address, b.Province))
            .ToListAsync(cancellationToken);
}

internal sealed record BranchResponse(string Code, string Name, string Address, string Province);
