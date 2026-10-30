using System.Security.Claims;
using Eshop.Api.Auth;
using Eshop.Api.Contracts;
using Eshop.Api.ThirdParty;
using Eshop.Data;
using Eshop.Domain.Applications;
using Eshop.Domain.Contracts;
using Eshop.Domain.Customers;
using Eshop.Domain.Handovers;
using Eshop.Domain.Offers;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Eshop.Api.Offers;

/// <summary>
/// Offers (Customer only, own Offers only). The Customer reads the Offer, asks for an SMS code and accepts with
/// it, which creates the Contract and its outbox entry in one transaction (ADR-0001); or declines. An Offer
/// not acted on expires 7 days after approval (on the <see cref="TimeProvider"/> clock).
/// </summary>
internal static class OfferEndpoints
{
    public static IEndpointRouteBuilder MapOfferEndpoints(this IEndpointRouteBuilder api)
    {
        var offers = api.MapGroup("/offers").WithTags("Offers").RequireAuthorization(Roles.Customer);
        offers.MapGet("/{id:int}", GetOffer);
        offers.MapPost("/{id:int}/sms-code", RequestAcceptanceCode);
        offers.MapPost("/{id:int}/accept", Accept);
        offers.MapPost("/{id:int}/decline", Decline);

        return api;
    }

    public static string StatusCode(OfferStatus status) =>
        status switch
        {
            OfferStatus.Open => "open",
            OfferStatus.Accepted => "accepted",
            OfferStatus.Declined => "declined",
            _ => "expired",
        };

    private static async Task<Results<Ok<OfferResponse>, NotFound>> GetOffer(
        int id, ClaimsPrincipal user, EshopDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var offer = await FindAsync(db, id, user.RequiredCustomerId(), cancellationToken);
        return offer is null ? TypedResults.NotFound() : TypedResults.Ok(await ToResponseAsync(db, offer, clock.GetUtcNow(), cancellationToken));
    }

    private static async Task<Results<Accepted, ProblemHttpResult, NotFound>> RequestAcceptanceCode(
        int id, ClaimsPrincipal user, EshopDbContext db, ISmsSender sms, TimeProvider clock, CancellationToken cancellationToken)
    {
        var offer = await FindAsync(db, id, user.RequiredCustomerId(), cancellationToken);
        if (offer is null)
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetUtcNow();
        if (await NotOpenAsync(db, offer, now, cancellationToken) is { } notOpen)
        {
            return notOpen;
        }

        var (smsCode, code) = SmsCode.Issue(offer.Customer.MobileNumber, SmsCodePurpose.OfferAcceptance, now);
        db.SmsCodes.Add(smsCode);
        await db.SaveChangesAsync(cancellationToken);

        var minutes = (int)SmsCode.Lifetime.TotalMinutes;
        await sms.SendAsync(
            offer.Customer.MobileNumber,
            $"รหัสยืนยันการยอมรับข้อเสนอเช่าซื้อ eShop คือ {code} (ใช้ได้ภายใน {minutes} นาที) ห้ามบอกรหัสนี้กับผู้อื่น",
            cancellationToken);

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Created<ContractResponse>, ProblemHttpResult, NotFound>> Accept(
        int id, AcceptOfferRequest request, ClaimsPrincipal user, EshopDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var customerId = user.RequiredCustomerId();
        var offer = await FindAsync(db, id, customerId, cancellationToken);
        if (offer is null)
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetUtcNow();
        if (await NotOpenAsync(db, offer, now, cancellationToken) is { } notOpen)
        {
            return notOpen;
        }

        // One transaction: the used SMS code, the per-month number, the accepted Offer, the Contract and its outbox entry.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var smsCode = await db.SmsCodes
            .Where(c => c.MobileNumber == offer.Customer.MobileNumber && c.Purpose == SmsCodePurpose.OfferAcceptance)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var check = smsCode?.Check(request.Code ?? string.Empty, now) ?? SmsCodeCheck.Invalid;
        if (check != SmsCodeCheck.Accepted)
        {
            // Keep the wrong-guess count.
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Refusal.SmsCode(check);
        }

        if (await db.Contracts.Where(Contract.IsActive).AnyAsync(c => c.CustomerId == customerId, cancellationToken))
        {
            return ActiveContract();
        }

        var month = ContractNumber.MonthOf(now);
        var sequence = (await db.Database
            .SqlQuery<int>($"""
                INSERT INTO contract_number_counters (month, last_number) VALUES ({month}, 1)
                ON CONFLICT (month) DO UPDATE SET last_number = contract_number_counters.last_number + 1
                RETURNING last_number AS "Value"
                """)
            .ToListAsync(cancellationToken)).Single();

        var contract = offer.Accept(new ContractNumber(month, sequence), now);
        db.Contracts.Add(contract);
        db.ContractOutbox.Add(ContractOutboxEntry.For(contract));
        var handover = Handover.For(contract);
        db.Handovers.Add(handover);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique)
        {
            // A concurrent accept won: this Offer (or another of the Customer's) already became a Contract.
            return unique.ConstraintName == "ix_contracts_offer_id"
                ? Refusal.Conflict("offer-accepted", "ข้อเสนอนี้ได้รับการยอมรับแล้ว")
                : ActiveContract();
        }

        return TypedResults.Created($"/api/contracts/{contract.Number}", ContractEndpoints.ToResponse(contract, handover));
    }

    private static async Task<Results<Ok<OfferResponse>, ProblemHttpResult, NotFound>> Decline(
        int id, ClaimsPrincipal user, EshopDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var offer = await FindAsync(db, id, user.RequiredCustomerId(), cancellationToken);
        if (offer is null)
        {
            return TypedResults.NotFound();
        }

        var now = clock.GetUtcNow();
        if (await NotOpenAsync(db, offer, now, cancellationToken) is { } notOpen)
        {
            return notOpen;
        }

        offer.Decline(now);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(db, offer, now, cancellationToken));
    }

    private static Task<Offer?> FindAsync(EshopDbContext db, int id, int customerId, CancellationToken cancellationToken) =>
        db.Offers
            .Include(o => o.Customer)
            .Include(o => o.Application).ThenInclude(a => a.Product)
            .Include(o => o.Application).ThenInclude(a => a.Branch)
            .SingleOrDefaultAsync(o => o.Id == id && o.CustomerId == customerId, cancellationToken);

    /// <summary>409 <c>offer-accepted|offer-declined|offer-expired</c> unless the Offer is open (an expiry is stored once noticed).</summary>
    private static async Task<ProblemHttpResult?> NotOpenAsync(EshopDbContext db, Offer offer, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var status = offer.StatusAt(now);
        if (status == OfferStatus.Open)
        {
            return null;
        }

        if (status == OfferStatus.Expired && offer.Status == OfferStatus.Open)
        {
            offer.NoteExpiry(now);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Refusal.Conflict($"offer-{StatusCode(status)}", status switch
        {
            OfferStatus.Accepted => "ข้อเสนอนี้ได้รับการยอมรับแล้ว",
            OfferStatus.Declined => "คุณได้ปฏิเสธข้อเสนอนี้แล้ว",
            _ => "ข้อเสนอนี้หมดอายุแล้ว",
        });
    }

    private static ProblemHttpResult ActiveContract() =>
        Refusal.Conflict("active-contract", "คุณมีสัญญาเช่าซื้อที่ยังไม่สิ้นสุดอยู่แล้ว");

    private static async Task<OfferResponse> ToResponseAsync(EshopDbContext db, Offer offer, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var a = offer.Application;
        var contractNumber = await db.Contracts.Where(c => c.OfferId == offer.Id).Select(c => c.Number).SingleOrDefaultAsync(cancellationToken);
        return new(
            offer.Id,
            StatusCode(offer.StatusAt(now)),
            offer.ExpiresAt,
            a.Id,
            new OfferProductResponse(a.Product.Id, a.Product.Name),
            a.Price,
            a.DownPayment,
            a.Term,
            a.Installment,
            a.TotalPayable,
            a.Handover == HandoverMethod.Delivery ? "delivery" : "store-pickup",
            a.DeliveryAddress,
            a.DeliveryProvince,
            a.Branch is null ? null : new OfferBranchResponse(a.Branch.Code, a.Branch.Name),
            contractNumber);
    }
}

internal sealed record AcceptOfferRequest(string? Code);

// Status: open | accepted (then ContractNumber is set) | declined | expired. Terms are the Application's, locked at submit.
internal sealed record OfferResponse(
    int Id,
    string Status,
    DateTimeOffset ExpiresAt,
    int ApplicationId,
    OfferProductResponse Product,
    decimal Price,
    decimal DownPayment,
    int Term,
    decimal Installment,
    decimal TotalPayable,
    string Handover,
    string? DeliveryAddress,
    string? DeliveryProvince,
    OfferBranchResponse? Branch,
    string? ContractNumber);

internal sealed record OfferProductResponse(int Id, string Name);

internal sealed record OfferBranchResponse(string Code, string Name);
