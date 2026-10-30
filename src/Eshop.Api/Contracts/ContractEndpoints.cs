using System.Security.Claims;
using Eshop.Api.Auth;
using Eshop.Data;
using Eshop.Domain.Applications;
using Eshop.Domain.Contracts;
using Eshop.Domain.Handovers;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Api.Contracts;

/// <summary>
/// The signed-in Customer's Contracts, by Contract number. Read-only: eShop never edits a Contract once
/// pushed (ADR-0001); status, cancellation and Payments come from the Enterprise System by sync and may lag
/// it by one sync interval (30 s).
/// </summary>
internal static class ContractEndpoints
{
    public static IEndpointRouteBuilder MapContractEndpoints(this IEndpointRouteBuilder api)
    {
        var contracts = api.MapGroup("/contracts").WithTags("Contracts").RequireAuthorization(Roles.Customer);
        contracts.MapGet("/", ListContracts);
        contracts.MapGet("/{number}", GetContract);

        return api;
    }

    private static async Task<List<ContractSummaryResponse>> ListContracts(
        ClaimsPrincipal user, EshopDbContext db, CancellationToken cancellationToken)
    {
        var customerId = user.RequiredCustomerId();
        var contracts = await Query(db)
            .Where(c => c.CustomerId == customerId)
            .OrderByDescending(c => c.AcceptedAt)
            .ToListAsync(cancellationToken);
        return [.. contracts.Select(c => new ContractSummaryResponse(
            c.Number, StatusCode(c.Status), c.AcceptedAt, new ContractProductResponse(c.Product.Id, c.Product.Name), c.Installment, c.Term))];
    }

    private static async Task<Results<Ok<ContractResponse>, NotFound>> GetContract(
        string number, ClaimsPrincipal user, EshopDbContext db, CancellationToken cancellationToken)
    {
        var customerId = user.RequiredCustomerId();
        var contract = await Query(db).SingleOrDefaultAsync(c => c.Number == number && c.CustomerId == customerId, cancellationToken);
        if (contract is null)
        {
            return TypedResults.NotFound();
        }

        var handover = await db.Handovers.AsNoTracking().SingleOrDefaultAsync(h => h.ContractId == contract.Id, cancellationToken);
        if (handover is not null)
        {
            handover.Contract = contract;
        }

        return TypedResults.Ok(ToResponse(contract, handover));
    }

    /// <summary>
    /// The Handover stage as the Customer sees it: <c>preparing | ready-for-pickup | out-for-delivery |
    /// handed-over</c>, or <c>stopped</c> once the Contract was cancelled before the goods were handed over.
    /// </summary>
    public static string StageCode(Handover handover) =>
        handover.IsStopped ? "stopped" : StageCode(handover.Stage);

    public static string StageCode(HandoverStage stage) =>
        stage switch
        {
            HandoverStage.Preparing => "preparing",
            HandoverStage.ReadyForPickup => "ready-for-pickup",
            HandoverStage.OutForDelivery => "out-for-delivery",
            _ => "handed-over",
        };

    private static IQueryable<Contract> Query(EshopDbContext db) =>
        db.Contracts
            .Include(c => c.Product)
            .Include(c => c.Application).ThenInclude(a => a.Branch)
            .Include(c => c.Payments)
            .AsSplitQuery()
            .AsNoTracking();

    public static string StatusCode(ContractStatus status) =>
        status switch
        {
            ContractStatus.Active => "active",
            ContractStatus.Overdue => "overdue",
            ContractStatus.PaidOff => "paid-off",
            _ => "cancelled",
        };

    public static string ChannelCode(string channel) =>
        channel switch
        {
            "COUNTER" => "counter",
            "TRANSFER" => "transfer",
            "PROMPTPAY" => "promptpay",
            _ => channel.ToLowerInvariant(),
        };

    /// <summary>Needs Product, Application (with Branch) and Payments loaded; <paramref name="handover"/> with its Contract.</summary>
    public static ContractResponse ToResponse(Contract c, Handover? handover) =>
        new(
            c.Number,
            StatusCode(c.Status),
            c.AcceptedAt,
            new ContractProductResponse(c.Product.Id, c.Product.Name),
            c.Price,
            c.DownPayment,
            c.Term,
            c.Installment,
            c.TotalPayable,
            c.Application.Handover == HandoverMethod.Delivery ? "delivery" : "store-pickup",
            c.Application.DeliveryAddress,
            c.Application.DeliveryProvince,
            c.Application.Branch is null ? null : new ContractBranchResponse(c.Application.Branch.Code, c.Application.Branch.Name),
            handover is null ? "preparing" : StageCode(handover),
            handover?.HandedOverAt,
            [.. c.Schedule(handover?.HandedOverOn).Select(i => new InstallmentResponse(i.Number, i.Amount, i.DueDate))],
            c.CancelledAt,
            [.. c.Payments.OrderBy(p => p.PaidAt).ThenBy(p => p.EnterprisePaymentId)
                .Select(p => new PaymentResponse(p.InstallmentNumber, p.Amount, p.PaidAt, ChannelCode(p.Channel)))],
            c.Payments.Sum(p => p.Amount));
}

// Status: active | overdue | paid-off | cancelled (as known by the Enterprise System).
// HandoverStage: preparing | ready-for-pickup | out-for-delivery | handed-over | stopped (cancelled before Handover).
// Installments: the schedule; DueDate stays null until Handover (the first is due one month after it).
// CancelledAt, Payments (oldest first; channel counter | transfer | promptpay) and AmountPaid: as the Enterprise
// System last reported them. eShop computes no balance or overdue state of its own.
internal sealed record ContractResponse(
    string Number,
    string Status,
    DateTimeOffset AcceptedAt,
    ContractProductResponse Product,
    decimal Price,
    decimal DownPayment,
    int Term,
    decimal Installment,
    decimal TotalPayable,
    string Handover,
    string? DeliveryAddress,
    string? DeliveryProvince,
    ContractBranchResponse? Branch,
    string HandoverStage,
    DateTimeOffset? HandedOverAt,
    List<InstallmentResponse> Installments,
    DateTimeOffset? CancelledAt,
    List<PaymentResponse> Payments,
    decimal AmountPaid);

internal sealed record PaymentResponse(int? InstallmentNumber, decimal Amount, DateTimeOffset PaidAt, string Channel);

internal sealed record ContractSummaryResponse(
    string Number, string Status, DateTimeOffset AcceptedAt, ContractProductResponse Product, decimal Installment, int Term);

internal sealed record ContractProductResponse(int Id, string Name);

internal sealed record ContractBranchResponse(string Code, string Name);

internal sealed record InstallmentResponse(int Number, decimal Amount, DateOnly? DueDate);
