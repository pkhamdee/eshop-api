using System.Security.Claims;
using Eshop.Api.Auth;
using Eshop.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Api.Auctions;

/// <summary>
/// Public Auction browsing: anyone may read the current Auction, its Lots and each Lot's current highest Bid
/// (Lot pages poll <c>GET /api/lots/{id}</c>). Bidding is in <see cref="BidEndpoints"/>.
/// </summary>
internal static class AuctionEndpoints
{
    public static IEndpointRouteBuilder MapAuctionEndpoints(this IEndpointRouteBuilder api)
    {
        var auctions = api.MapGroup("/auctions").WithTags("Auctions");
        auctions.MapGet("/current", GetCurrentAuction);
        auctions.MapGet("/{id:int}", GetAuction);

        api.MapGet("/lots/{id:int}", GetLot).WithTags("Auctions");
        api.MapGet("/lots/won", GetWonLots).WithTags("Auctions").RequireAuthorization(Roles.Customer);

        return api;
    }

    // The signed-in Customer's won Lots (closed Auctions where their Bid was the highest), most recent close first:
    // awaiting payment (pay cash at a Branch by paymentDueAt), sold or forfeited.
    private static async Task<List<LotResponse>> GetWonLots(
        ClaimsPrincipal user, EshopDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var customerId = user.RequiredCustomerId();
        var now = clock.GetUtcNow();
        var lots = await db.Lots.Include(l => l.Auction).AsNoTracking()
            .Where(l => l.HighestBidderId == customerId && l.Auction.ClosesAt <= now)
            .OrderByDescending(l => l.Auction.ClosesAt).ThenByDescending(l => l.Id)
            .ToListAsync(cancellationToken);
        return lots.Select(l => LotResponse.Of(l, l.Auction, now, user)).ToList();
    }

    // The open Auction (the latest-opened one if Staff made several), else the next upcoming one, else the
    // one that closed last. 404 when there has never been an Auction.
    private static async Task<Results<Ok<AuctionResponse>, NotFound>> GetCurrentAuction(
        ClaimsPrincipal user, EshopDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var auctions = db.Auctions.Include(a => a.Lots).AsNoTracking();
        var auction =
            await auctions.Where(a => a.OpensAt <= now && a.ClosesAt > now)
                .OrderByDescending(a => a.OpensAt).ThenByDescending(a => a.Id).FirstOrDefaultAsync(cancellationToken)
            ?? await auctions.Where(a => a.OpensAt > now)
                .OrderBy(a => a.OpensAt).ThenBy(a => a.Id).FirstOrDefaultAsync(cancellationToken)
            ?? await auctions.OrderByDescending(a => a.ClosesAt).ThenByDescending(a => a.Id).FirstOrDefaultAsync(cancellationToken);
        return auction is null ? TypedResults.NotFound() : TypedResults.Ok(AuctionResponse.Of(auction, now, user));
    }

    private static async Task<Results<Ok<AuctionResponse>, NotFound>> GetAuction(
        int id, ClaimsPrincipal user, EshopDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var auction = await db.Auctions.Include(a => a.Lots).AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        return auction is null ? TypedResults.NotFound() : TypedResults.Ok(AuctionResponse.Of(auction, clock.GetUtcNow(), user));
    }

    private static async Task<Results<Ok<LotResponse>, NotFound>> GetLot(
        int id, ClaimsPrincipal user, EshopDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var lot = await db.Lots.Include(l => l.Auction).AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        return lot is null ? TypedResults.NotFound() : TypedResults.Ok(LotResponse.Of(lot, lot.Auction, clock.GetUtcNow(), user));
    }
}
