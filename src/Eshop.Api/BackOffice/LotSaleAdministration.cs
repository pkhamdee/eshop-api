using System.Security.Claims;
using Eshop.Api.Auctions;
using Eshop.Api.Auth;
using Eshop.Data;
using Eshop.Domain.Auctions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Api.BackOffice;

/// <summary>
/// After an Auction closes, the highest Bid on each Lot wins and the winner has 3 days to pay cash at a Branch. Staff
/// record that payment together with the Handover of the iPhone, or, once the 3 days have passed unpaid, mark the Lot
/// Forfeited. A Forfeited Lot never passes to the second-highest bidder; Staff may offer it (or an unsold Lot) again
/// in a later Auction, which creates a new Lot there. Won Lots are cash-only: there is no hire-purchase path.
/// <para>
/// Payment and forfeiture race each other at the counter, so each is ONE conditional UPDATE that only matches a Lot
/// that is neither paid nor Forfeited: whichever commits first wins and the other gets 409.
/// </para>
/// </summary>
internal static class LotSaleAdministration
{
    public static void MapLotSaleAdministration(this RouteGroupBuilder backOffice)
    {
        backOffice.MapPost("/lots/{id:int}/payment", RecordPayment);
        backOffice.MapPost("/lots/{id:int}/forfeit", Forfeit);
        backOffice.MapPost("/lots/{id:int}/re-offer", Reoffer);
    }

    private static async Task<Results<Ok<LotResponse>, NotFound, ValidationProblem, ProblemHttpResult>> RecordPayment(
        int id, PaymentRequest request, ClaimsPrincipal user, EshopDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var lot = await db.Lots.Include(l => l.Auction).AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lot is null)
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetUtcNow();
        if (lot.StatusAt(now) != LotStatus.AwaitingPayment)
        {
            return NotAwaitingPayment();
        }

        var branch = await db.Branches.AsNoTracking()
            .SingleOrDefaultAsync(b => b.Code == request.BranchCode && !b.IsRetired, cancellationToken);
        if (branch is null)
        {
            var errors = BackOfficeEndpoints.Errors();
            errors.Require(false, "branchCode", "กรุณาเลือกสาขาที่รับชำระเงิน");
            return TypedResults.ValidationProblem(errors);
        }

        var recordedBy = user.RequiredStaffUsername();
        var updated = await db.Lots
            .Where(l => l.Id == id && l.HighestBidderId != null && l.PaidAt == null && l.ForfeitedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.PaidAt, now)
                .SetProperty(l => l.PaymentBranchId, branch.Id)
                .SetProperty(l => l.PaymentRecordedBy, recordedBy), cancellationToken);
        if (updated == 0)
        {
            return NotAwaitingPayment();
        }

        return TypedResults.Ok(await StaffViewAsync(db, id, now, cancellationToken));
    }

    private static async Task<Results<Ok<LotResponse>, NotFound, ProblemHttpResult>> Forfeit(
        int id, EshopDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var lot = await db.Lots.Include(l => l.Auction).AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lot is null)
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetUtcNow();
        if (lot.StatusAt(now) != LotStatus.AwaitingPayment)
        {
            return NotAwaitingPayment();
        }

        if (!lot.MayBeForfeitedAt(now))
        {
            return Refusal.Conflict("payment-window-open", "ผู้ชนะยังมีเวลาชำระเงินภายใน 3 วัน", ("paymentDueAt", lot.PaymentDueAt));
        }

        var updated = await db.Lots
            .Where(l => l.Id == id && l.HighestBidderId != null && l.PaidAt == null && l.ForfeitedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ForfeitedAt, now), cancellationToken);
        if (updated == 0)
        {
            return NotAwaitingPayment();
        }

        return TypedResults.Ok(await StaffViewAsync(db, id, now, cancellationToken));
    }

    // Offers a Forfeited or unsold Lot again as a new Lot in a later (not yet closed) Auction, with the same iPhone,
    // photos and condition notes; the starting price may be changed. Each Lot can be offered again once.
    private static async Task<Results<Created<LotResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Reoffer(
        int id, ReofferRequest request, EshopDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var lot = await db.Lots.Include(l => l.Auction).AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lot is null)
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetUtcNow();
        var target = await db.Auctions.SingleOrDefaultAsync(a => a.Id == request.AuctionId, cancellationToken);
        var startingPrice = request.StartingPrice ?? lot.StartingPrice;
        var errors = BackOfficeEndpoints.Errors();
        errors.Require(target is not null, "auctionId", "ไม่พบการประมูลที่เลือก");
        errors.Require(startingPrice is > 0m and < 10_000_000m && decimal.Truncate(startingPrice) == startingPrice,
            "startingPrice", "ราคาเริ่มต้นต้องเป็นจำนวนเต็มบาทที่มากกว่า 0");
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!lot.MayBeReofferedAt(now))
        {
            return Refusal.Conflict("lot-not-reofferable", "นำรายการนี้กลับมาประมูลใหม่ไม่ได้ (เฉพาะรายการที่ถูกริบสิทธิ์หรือไม่มีผู้ประมูล และยังไม่เคยนำกลับมา)");
        }

        if (target!.StatusAt(now) == AuctionStatus.Closed)
        {
            return Refusal.Conflict("auction-closed", "การประมูลนี้ปิดแล้ว");
        }

        var again = new Lot
        {
            AuctionId = target.Id,
            Auction = target,
            Title = lot.Title,
            ConditionNotes = lot.ConditionNotes,
            StartingPrice = startingPrice,
            ImageIds = [.. lot.ImageIds],
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Lots.Add(again);
        await db.SaveChangesAsync(cancellationToken);
        var linked = await db.Lots.Where(l => l.Id == id && l.ReofferedAsLotId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ReofferedAsLotId, again.Id), cancellationToken);
        if (linked == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Refusal.Conflict("lot-not-reofferable", "รายการนี้ถูกนำกลับมาประมูลแล้ว");
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Created($"/api/lots/{again.Id}", LotResponse.Of(again, target, now, forStaff: true));
    }

    /// <summary>A Lot with its winner and payment details, as Staff see it.</summary>
    internal static IQueryable<Lot> WithSale(this IQueryable<Lot> lots) =>
        lots.Include(l => l.Auction).Include(l => l.HighestBidder).Include(l => l.PaymentBranch).AsNoTracking();

    private static async Task<LotResponse> StaffViewAsync(EshopDbContext db, int id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var lot = await db.Lots.WithSale().SingleAsync(l => l.Id == id, cancellationToken);
        return LotResponse.Of(lot, lot.Auction, now, forStaff: true);
    }

    private static ProblemHttpResult NotAwaitingPayment() =>
        Refusal.Conflict("lot-not-awaiting-payment", "รายการนี้ไม่ได้รอการชำระเงินจากผู้ชนะ");
}

// The Branch where the winner paid cash and took the iPhone (payment and Handover happen together at the counter).
internal sealed record PaymentRequest(string? BranchCode);

// AuctionId: a later Auction that has not closed. StartingPrice: whole baht; defaults to the Lot's starting price.
internal sealed record ReofferRequest(int AuctionId, decimal? StartingPrice);
