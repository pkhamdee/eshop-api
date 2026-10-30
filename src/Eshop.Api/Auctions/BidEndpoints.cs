using System.Security.Claims;
using Eshop.Api.Auth;
using Eshop.Data;
using Eshop.Domain.Applications;
using Eshop.Domain.Auctions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Api.Auctions;

/// <summary>
/// Customers who have passed ID Verification and are aged 20 or over place Bids while the Auction is open. A Bid must be at least the
/// starting price (first Bid) or at least ฿100 above the current highest Bid.
/// <para>
/// No ties, even under concurrency: the Lot's highest Bid is raised by ONE conditional UPDATE whose WHERE clause
/// re-checks the increment rule against the row as it is in the database. PostgreSQL row-locks the Lot for the
/// UPDATE, and a concurrent Bid waiting on that lock re-evaluates the WHERE clause against the committed new
/// highest Bid (READ COMMITTED re-check), so of two equal Bids exactly one matches. A unique index on
/// (lot_id, amount) in <c>bids</c> backs this up.
/// </para>
/// </summary>
internal static class BidEndpoints
{
    public static IEndpointRouteBuilder MapBidEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/lots/{id:int}/bids", PlaceBid).WithTags("Auctions").RequireAuthorization(Roles.Customer);
        return api;
    }

    private static async Task<Results<Created<LotResponse>, NotFound, ProblemHttpResult>> PlaceBid(
        int id, BidRequest request, ClaimsPrincipal user, EshopDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var customerId = user.RequiredCustomerId();
        var amount = request.Amount;
        if (amount is <= 0m or >= 10_000_000m || decimal.Truncate(amount) != amount)
        {
            return Refusal.BadRequest("bid-amount-invalid", "ราคาที่เสนอต้องเป็นจำนวนเต็มบาท");
        }

        var lot = await db.Lots.Include(l => l.Auction).AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lot is null)
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetUtcNow();
        var bidder = await db.Customers.AsNoTracking().Where(c => c.Id == customerId)
            .Select(c => new { c.IdVerifiedAt, c.DateOfBirth }).SingleAsync(cancellationToken);
        if (bidder.IdVerifiedAt is null || bidder.DateOfBirth is not { } dateOfBirth)
        {
            return Refusal.Of(StatusCodes.Status403Forbidden, "id-verification-required",
                "กรุณายืนยันตัวตนด้วยบัตรประชาชนก่อนร่วมประมูล");
        }

        // Passing ID Verification is not enough: like hire purchase, bidding needs the Customer to be 20 or over.
        if (!Eligibility.IsOldEnough(dateOfBirth, now))
        {
            return Refusal.Of(StatusCodes.Status403Forbidden, "under-20", "ผู้ร่วมประมูลต้องมีอายุ 20 ปีขึ้นไป");
        }

        // An Auction's open and close times never change once created, so checking them here is final.
        switch (lot.Auction.StatusAt(now))
        {
            case AuctionStatus.Upcoming:
                return Refusal.Conflict("auction-not-open", "การประมูลยังไม่เปิด");
            case AuctionStatus.Closed:
                return Refusal.Conflict("auction-closed", "การประมูลปิดแล้ว");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var raised = await db.Lots
            .Where(l => l.Id == id
                && ((l.HighestBid == null && amount >= l.StartingPrice)
                    || (l.HighestBid != null && amount >= l.HighestBid + Lot.MinimumIncrement)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.HighestBid, amount)
                .SetProperty(l => l.HighestBidderId, customerId)
                .SetProperty(l => l.BidCount, l => l.BidCount + 1), cancellationToken);

        if (raised == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var current = await db.Lots.AsNoTracking().Where(l => l.Id == id)
                .Select(l => new { l.HighestBid, l.StartingPrice }).SingleAsync(cancellationToken);
            return Refusal.Conflict("bid-too-low",
                "ราคาที่เสนอต้องสูงกว่าราคาสูงสุดปัจจุบันอย่างน้อย 100 บาท",
                ("minimumBid", Lot.MinimumBidOver(current.HighestBid, current.StartingPrice)));
        }

        db.Bids.Add(new Bid { LotId = id, CustomerId = customerId, Amount = amount, PlacedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // The Lot as it is now (someone may already have outbid this Bid).
        var after = await db.Lots.Include(l => l.Auction).AsNoTracking().SingleAsync(l => l.Id == id, cancellationToken);
        return TypedResults.Created($"/api/lots/{id}", LotResponse.Of(after, after.Auction, now, user));
    }
}

// Amount in whole baht.
internal sealed record BidRequest(decimal Amount);
