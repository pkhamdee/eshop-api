using System.Text.RegularExpressions;
using Eshop.Data;
using Eshop.Domain.Branches;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Api.BackOffice;

/// <summary>
/// Staff add, edit and retire Branches. A Branch's code never changes (Handovers refer to it); a retired
/// Branch stays in the database but is no longer offered for Store Pickup.
/// </summary>
internal static partial class BranchAdministration
{
    public static void MapBranchAdministration(this RouteGroupBuilder backOffice)
    {
        var branches = backOffice.MapGroup("/branches");
        branches.MapGet("/", ListBranches);
        branches.MapPost("/", AddBranch);
        branches.MapPut("/{code}", EditBranch);
        branches.MapPost("/{code}/retire", (string code, EshopDbContext db, CancellationToken ct) => SetRetired(code, true, db, ct));
        branches.MapPost("/{code}/reinstate", (string code, EshopDbContext db, CancellationToken ct) => SetRetired(code, false, db, ct));
    }

    private static async Task<List<BackOfficeBranchResponse>> ListBranches(EshopDbContext db, CancellationToken cancellationToken) =>
        await db.Branches
            .OrderBy(b => b.IsRetired).ThenBy(b => b.Id)
            .Select(b => new BackOfficeBranchResponse(b.Code, b.Name, b.Address, b.Province, b.IsRetired))
            .ToListAsync(cancellationToken);

    private static async Task<Results<Created<BackOfficeBranchResponse>, ValidationProblem, ProblemHttpResult>> AddBranch(
        NewBranchRequest request, EshopDbContext db, CancellationToken cancellationToken)
    {
        var errors = Validate(request.Name, request.Address, request.Province);
        errors.Require(request.Code is not null && BranchCode().IsMatch(request.Code), "code",
            "รหัสสาขาใช้ได้เฉพาะ a-z, 0-9 และ - (ไม่เกิน 50 ตัวอักษร)");
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await db.Branches.AnyAsync(b => b.Code == request.Code, cancellationToken))
        {
            return Refusal.Conflict("branch-code-taken", "มีรหัสสาขานี้อยู่แล้ว");
        }

        var branch = new Branch
        {
            Code = request.Code!,
            Name = request.Name!.Trim(),
            Address = request.Address!.Trim(),
            Province = request.Province!.Trim(),
        };
        db.Branches.Add(branch);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/back-office/branches/{branch.Code}", ToResponse(branch));
    }

    private static async Task<Results<Ok<BackOfficeBranchResponse>, NotFound, ValidationProblem>> EditBranch(
        string code, BranchRequest request, EshopDbContext db, CancellationToken cancellationToken)
    {
        var branch = await db.Branches.SingleOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (branch is null)
        {
            return TypedResults.NotFound();
        }

        var errors = Validate(request.Name, request.Address, request.Province);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        branch.Name = request.Name!.Trim();
        branch.Address = request.Address!.Trim();
        branch.Province = request.Province!.Trim();
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(branch));
    }

    private static async Task<Results<NoContent, NotFound>> SetRetired(
        string code, bool retired, EshopDbContext db, CancellationToken cancellationToken)
    {
        var branch = await db.Branches.SingleOrDefaultAsync(b => b.Code == code, cancellationToken);
        if (branch is null)
        {
            return TypedResults.NotFound();
        }

        branch.IsRetired = retired;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static Dictionary<string, string[]> Validate(string? name, string? address, string? province)
    {
        var errors = BackOfficeEndpoints.Errors();
        errors.Require(!string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200, "name", "กรุณาระบุชื่อสาขา (ไม่เกิน 200 ตัวอักษร)");
        errors.Require(!string.IsNullOrWhiteSpace(address) && address.Trim().Length <= 500, "address", "กรุณาระบุที่อยู่ (ไม่เกิน 500 ตัวอักษร)");
        errors.Require(!string.IsNullOrWhiteSpace(province) && province.Trim().Length <= 100, "province", "กรุณาระบุจังหวัด");
        return errors;
    }

    private static BackOfficeBranchResponse ToResponse(Branch b) => new(b.Code, b.Name, b.Address, b.Province, b.IsRetired);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,49}$")]
    private static partial Regex BranchCode();
}

internal sealed record NewBranchRequest(string? Code, string? Name, string? Address, string? Province);

internal sealed record BranchRequest(string? Name, string? Address, string? Province);

internal sealed record BackOfficeBranchResponse(string Code, string Name, string Address, string Province, bool IsRetired);
