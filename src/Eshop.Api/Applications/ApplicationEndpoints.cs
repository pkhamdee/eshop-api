using System.Security.Claims;
using System.Text.RegularExpressions;
using Eshop.Api.Auth;
using Eshop.Api.Images;
using Eshop.Api.Offers;
using Eshop.Data;
using Eshop.Domain.Applications;
using Eshop.Domain.Catalog;
using Eshop.Domain.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Api.Applications;

/// <summary>
/// Eligibility and Applications (Customer only). Submitting checks Eligibility first (a failing Customer gets
/// 409 and no Application), stores the ID card photo privately and returns 202; the Credit Decision is made in
/// the background (<see cref="CreditDecisionWorker"/>) and the Customer checks back with GET.
/// </summary>
internal static partial class ApplicationEndpoints
{
    private static readonly IReadOnlySet<string> PhotoTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/eligibility", GetEligibility).WithTags("Applications").RequireAuthorization(Roles.Customer);
        api.MapGet("/delivery-provinces", () => BangkokMetropolitanRegion.Provinces).WithTags("Applications");

        var applications = api.MapGroup("/applications").WithTags("Applications").RequireAuthorization(Roles.Customer);
        // Bearer-token API (no cookies), so antiforgery tokens are not needed for the form post.
        applications.MapPost("/", Submit).DisableAntiforgery();
        applications.MapGet("/{id:int}", GetApplication);

        return api;
    }

    private static async Task<EligibilityResponse> GetEligibility(
        ClaimsPrincipal user, EshopDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var customerId = user.RequiredCustomerId();
        var verifiedIdNumber = await db.Customers.Where(c => c.Id == customerId).Select(c => c.IdNumber).SingleAsync(cancellationToken);
        var eligibility = await CheckEligibilityAsync(db, customerId, verifiedIdNumber, clock.GetUtcNow(), cancellationToken);
        return new EligibilityResponse(
            eligibility.IsEligible, ReasonCode(eligibility.Reason), eligibility.ReapplyFrom, eligibility.OfferId);
    }

    /// <summary>
    /// Eligibility from this Customer's Applications (with their Offers) and Contracts, and any made with
    /// <paramref name="idNumber"/> under another mobile number.
    /// </summary>
    private static async Task<Eligibility> CheckEligibilityAsync(
        EshopDbContext db, int customerId, string? idNumber, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var earlier = await db.Applications
            .Include(a => a.Offer)
            .Where(a => a.CustomerId == customerId || (idNumber != null && a.IdNumber == idNumber))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var hasActiveContract = await db.Contracts.Where(Contract.IsActive).AnyAsync(
            c => c.CustomerId == customerId || (idNumber != null && c.Application.IdNumber == idNumber),
            cancellationToken);
        return Eligibility.Check(earlier, hasActiveContract, now);
    }

    private static async Task<Results<Accepted<ApplicationResponse>, ProblemHttpResult, NotFound>> Submit(
        [FromForm] ApplicationForm form, ClaimsPrincipal user, EshopDbContext db, IImageStore images,
        CreditDecisionQueue decisions, TimeProvider clock, CancellationToken cancellationToken)
    {
        var customerId = user.RequiredCustomerId();

        if (form.IdNumber is not { } idNumber || !ThaiIdNumber().IsMatch(idNumber))
        {
            return Refusal.BadRequest("id-number-invalid", "เลขประจำตัวประชาชนต้องเป็นตัวเลข 13 หลัก");
        }

        if (form.IdCardPhoto is not { Length: > 0 } photo)
        {
            return Refusal.BadRequest("photo-required", "กรุณาแนบรูปถ่ายบัตรประชาชน");
        }

        if (!PhotoTypes.Contains(photo.ContentType) || photo.Length > ImageTypes.MaxBytes)
        {
            return Refusal.BadRequest("photo-invalid", "รูปบัตรประชาชนต้องเป็นไฟล์ JPEG, PNG หรือ WebP ขนาดไม่เกิน 10 MB");
        }

        var product = await db.Products.Include(p => p.ProductCategory)
            .SingleOrDefaultAsync(p => p.Id == form.ProductId && !p.IsRetired, cancellationToken);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        var plan = form.Term is { } term && form.DownPayment is { } downPayment
            ? InstallmentPlan.For(product.Price, product.ProductCategory, term, downPayment)
            : null;
        if (plan is null)
        {
            return Refusal.BadRequest("plan-not-offered", "ไม่มีแผนผ่อนชำระตามจำนวนงวดและเงินดาวน์ที่เลือก");
        }

        HandoverChoice handover;
        switch (form.Handover)
        {
            case "delivery":
                if (string.IsNullOrWhiteSpace(form.DeliveryAddress))
                {
                    return Refusal.BadRequest("delivery-address-required", "กรุณากรอกที่อยู่สำหรับจัดส่ง");
                }

                if (!BangkokMetropolitanRegion.Contains(form.DeliveryProvince))
                {
                    return Refusal.BadRequest("delivery-outside-bmr", "จัดส่งได้เฉพาะกรุงเทพฯ และปริมณฑล กรุณาเลือกรับสินค้าที่สาขา");
                }

                handover = HandoverChoice.Delivery(form.DeliveryAddress.Trim(), form.DeliveryProvince!.Trim());
                break;
            case "store-pickup":
                var branch = await db.Branches.SingleOrDefaultAsync(b => b.Code == form.BranchCode, cancellationToken);
                if (branch is null)
                {
                    return Refusal.BadRequest("branch-not-found", "กรุณาเลือกสาขาที่จะรับสินค้า");
                }

                handover = HandoverChoice.StorePickup(branch);
                break;
            default:
                return Refusal.BadRequest("handover-invalid", "กรุณาเลือกจัดส่งหรือรับสินค้าที่สาขา");
        }

        // Eligibility before anything is stored: this Customer's Applications and any made with this ID number.
        var now = clock.GetUtcNow();
        var eligibility = await CheckEligibilityAsync(db, customerId, idNumber, now, cancellationToken);
        if (!eligibility.IsEligible)
        {
            return Refusal.Conflict(ReasonCode(eligibility.Reason)!, IneligibleTitle(eligibility.Reason),
                ("reapplyFrom", eligibility.ReapplyFrom), ("offerId", eligibility.OfferId));
        }

        string imageId;
        await using (var content = photo.OpenReadStream())
        {
            imageId = await images.SaveAsync(ImageArea.Private, content, photo.ContentType, cancellationToken);
        }

        var customer = await db.Customers.SingleAsync(c => c.Id == customerId, cancellationToken);
        var application = Application.Submit(customer, product, plan, handover, idNumber, imageId, photo.ContentType, now);
        db.Applications.Add(application);
        await db.SaveChangesAsync(cancellationToken);
        decisions.Enqueue(application.Id);

        return TypedResults.Accepted($"/api/applications/{application.Id}", ToResponse(application, now));
    }

    /// <summary>
    /// The Application, or (when ID Verification showed the Customer is under 20) 410 Gone with
    /// <c>error: under-20</c>: that submission failed Eligibility and never became an Application.
    /// </summary>
    private static async Task<Results<Ok<ApplicationResponse>, ProblemHttpResult, NotFound>> GetApplication(
        int id, ClaimsPrincipal user, EshopDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var customerId = user.RequiredCustomerId();
        var application = await db.Applications
            .Include(a => a.Product)
            .Include(a => a.Branch)
            .Include(a => a.Offer)
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == id && a.CustomerId == customerId, cancellationToken);
        if (application is not null)
        {
            return TypedResults.Ok(ToResponse(application, clock.GetUtcNow()));
        }

        var refused = await db.EligibilityRefusals
            .AnyAsync(r => r.SubmissionId == id && r.CustomerId == customerId, cancellationToken);
        return refused
            ? Refusal.Of(StatusCodes.Status410Gone, ReasonCode(IneligibleReason.Under20)!, IneligibleTitle(IneligibleReason.Under20))
            : TypedResults.NotFound();
    }

    private static ApplicationResponse ToResponse(Application a, DateTimeOffset now) =>
        new(
            a.Id,
            a.Status switch
            {
                ApplicationStatus.Reviewing => "reviewing",
                ApplicationStatus.Approved => "approved",
                _ => "rejected",
            },
            a.Reason switch
            {
                null => null,
                ApplicationReason.IdVerificationFailed => "id-verification-failed",
                ApplicationReason.CreditCheckFailed => "credit-check-failed",
                ApplicationReason.OverCreditLimit => "over-credit-limit",
                _ => "timed-out",
            },
            a.ReapplyFrom,
            a.SubmittedAt,
            a.DecidedAt,
            new ApplicationProductResponse(a.Product.Id, a.Product.Name),
            a.Price,
            a.DownPayment,
            a.Term,
            a.Installment,
            a.TotalPayable,
            a.Handover == HandoverMethod.Delivery ? "delivery" : "store-pickup",
            a.DeliveryAddress,
            a.DeliveryProvince,
            a.Branch is null ? null : new ApplicationBranchResponse(a.Branch.Code, a.Branch.Name),
            a.FullName,
            a.DateOfBirth,
            a.CreditLimit,
            a.Offer is null ? null : new ApplicationOfferResponse(a.Offer.Id, OfferEndpoints.StatusCode(a.Offer.StatusAt(now)), a.Offer.ExpiresAt));

    private static string? ReasonCode(IneligibleReason? reason) =>
        reason switch
        {
            null => null,
            IneligibleReason.ActiveContract => "active-contract",
            IneligibleReason.OfferOpen => "offer-open",
            IneligibleReason.ApplicationInProgress => "application-in-progress",
            IneligibleReason.Under20 => "under-20",
            _ => "recently-rejected",
        };

    private static string IneligibleTitle(IneligibleReason? reason) =>
        reason switch
        {
            IneligibleReason.ActiveContract => "คุณมีสัญญาเช่าซื้อที่ยังไม่สิ้นสุด จึงยังสมัครใหม่ไม่ได้",
            IneligibleReason.OfferOpen => "คุณมีข้อเสนอที่รอการตอบรับอยู่ กรุณายอมรับหรือปฏิเสธข้อเสนอก่อนสมัครใหม่",
            IneligibleReason.ApplicationInProgress => "คำขอก่อนหน้าของคุณกำลังอยู่ระหว่างพิจารณา",
            IneligibleReason.Under20 => "ผู้สมัครต้องมีอายุ 20 ปีขึ้นไป",
            _ => "คำขอของคุณถูกปฏิเสธเมื่อไม่ถึง 30 วันที่ผ่านมา",
        };

    [GeneratedRegex("^[0-9]{13}$")]
    private static partial Regex ThaiIdNumber();
}

/// <summary>multipart/form-data fields of an Application. Handover: <c>delivery</c> or <c>store-pickup</c>.</summary>
internal sealed class ApplicationForm
{
    public int ProductId { get; set; }

    public int? Term { get; set; }

    public decimal? DownPayment { get; set; }

    public string? IdNumber { get; set; }

    public IFormFile? IdCardPhoto { get; set; }

    public string? Handover { get; set; }

    public string? BranchCode { get; set; }

    public string? DeliveryAddress { get; set; }

    public string? DeliveryProvince { get; set; }
}

// Reason: active-contract | offer-open (then OfferId is set) | application-in-progress | recently-rejected (then
// ReapplyFrom is set).
internal sealed record EligibilityResponse(bool Eligible, string? Reason, DateTimeOffset? ReapplyFrom, int? OfferId);

// Status: reviewing | approved | rejected. (Under 20: GET answers 410 error under-20; no Application exists.)
// Reason: id-verification-failed | credit-check-failed | over-credit-limit | timed-out (Rejected).
// Offer: the Approved Application's Offer (status open | accepted | declined | expired).
// ReapplyFrom: when a Rejected Customer may apply again (null after a time-out). Price etc. are locked at submit.
internal sealed record ApplicationResponse(
    int Id,
    string Status,
    string? Reason,
    DateTimeOffset? ReapplyFrom,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DecidedAt,
    ApplicationProductResponse Product,
    decimal Price,
    decimal DownPayment,
    int Term,
    decimal Installment,
    decimal TotalPayable,
    string Handover,
    string? DeliveryAddress,
    string? DeliveryProvince,
    ApplicationBranchResponse? Branch,
    string? FullName,
    DateOnly? DateOfBirth,
    decimal? CreditLimit,
    ApplicationOfferResponse? Offer);

internal sealed record ApplicationOfferResponse(int Id, string Status, DateTimeOffset ExpiresAt);

internal sealed record ApplicationProductResponse(int Id, string Name);

internal sealed record ApplicationBranchResponse(string Code, string Name);
