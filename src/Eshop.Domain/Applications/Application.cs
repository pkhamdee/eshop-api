using Eshop.Domain.Branches;
using Eshop.Domain.Catalog;
using Eshop.Domain.Customers;
using Eshop.Domain.Offers;

namespace Eshop.Domain.Applications;

public enum ApplicationStatus
{
    /// <summary>Submitted; ID Verification and Credit Check are running in the background ("กำลังพิจารณา").</summary>
    Reviewing,

    /// <summary>Credit Decision: Approved. It comes with an <see cref="Offers.Offer"/>.</summary>
    Approved,

    /// <summary>Credit Decision: Rejected (see <see cref="Application.Reason"/>).</summary>
    Rejected,
}

/// <summary>Why an Application was Rejected.</summary>
public enum ApplicationReason
{
    IdVerificationFailed,
    CreditCheckFailed,
    OverCreditLimit,

    /// <summary>No Credit Decision within <see cref="Application.DecisionTimeLimit"/>; does not start the 30-day wait.</summary>
    TimedOut,
}

/// <summary>What happens after ID Verification has answered.</summary>
public enum IdVerificationOutcome
{
    /// <summary>Verified and aged 20 or over: the Credit Check follows.</summary>
    CreditCheckNext,

    /// <summary>ID Verification failed: Rejected.</summary>
    Rejected,

    /// <summary>
    /// Verified but under 20: Eligibility failed, so this submission is not an Application at all. The caller
    /// deletes it and records an <see cref="EligibilityRefusal"/> instead (no Credit Decision, no 30-day wait).
    /// </summary>
    Under20,
}

public enum HandoverMethod
{
    Delivery,
    StorePickup,
}

/// <summary>
/// A Customer's request to buy one Product on hire purchase with a chosen Term and Down Payment, decided
/// using their national ID card alone (GLOSSARY: Application). The price, Flat Rate and Installment are
/// locked when it is submitted.
/// </summary>
public class Application
{
    /// <summary>A Credit Decision not reached this long after submitting is Rejected as timed out.</summary>
    public static readonly TimeSpan DecisionTimeLimit = TimeSpan.FromSeconds(60);

    /// <summary>After a Rejected Application (other than a time-out) the Customer waits this long to reapply.</summary>
    public static readonly TimeSpan ReapplyWait = TimeSpan.FromDays(30);

    public int Id { get; set; }

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>The Product's price when the Application was submitted (later price changes don't apply).</summary>
    public decimal Price { get; set; }

    /// <summary>The Product Category's Flat Rate when the Application was submitted.</summary>
    public decimal FlatRate { get; set; }

    public int Term { get; set; }

    public decimal DownPayment { get; set; }

    public decimal Installment { get; set; }

    /// <summary>Down Payment + Installment × Term.</summary>
    public decimal TotalPayable { get; set; }

    public HandoverMethod Handover { get; set; }

    /// <summary>Delivery address (Delivery only), in the Bangkok Metropolitan Region.</summary>
    public string? DeliveryAddress { get; set; }

    /// <summary>Delivery province (Delivery only), one of <see cref="BangkokMetropolitanRegion.Provinces"/>.</summary>
    public string? DeliveryProvince { get; set; }

    /// <summary>Branch for Store Pickup.</summary>
    public int? BranchId { get; set; }

    public Branch? Branch { get; set; }

    /// <summary>National ID number as entered (13 digits).</summary>
    public required string IdNumber { get; set; }

    /// <summary>ID of the ID card photo in the image service's PRIVATE area. Never exposed by a link.</summary>
    public required string IdCardImageId { get; set; }

    /// <summary>Content type of the ID card photo, e.g. <c>image/jpeg</c>.</summary>
    public required string IdCardContentType { get; set; }

    /// <summary>Name from ID Verification (never typed by the Customer).</summary>
    public string? FullName { get; set; }

    /// <summary>Date of birth from ID Verification.</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Credit Limit returned by a passed Credit Check.</summary>
    public decimal? CreditLimit { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    public ApplicationStatus Status { get; set; }

    public ApplicationReason? Reason { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>The Offer of an Approved Application (created with the Credit Decision); null otherwise.</summary>
    public Offer? Offer { get; set; }

    public DateTimeOffset DecisionDeadline => SubmittedAt + DecisionTimeLimit;

    /// <summary>Price − Down Payment.</summary>
    public decimal FinancedAmount => Price - DownPayment;

    /// <summary>When the Customer may apply again after this Application, or null when it imposes no wait.</summary>
    public DateTimeOffset? ReapplyFrom =>
        Status == ApplicationStatus.Rejected && Reason != ApplicationReason.TimedOut ? DecidedAt + ReapplyWait : null;

    /// <summary>Submits an Application for an offered <paramref name="plan"/>, locking the price and Flat Rate.</summary>
    public static Application Submit(
        Customer customer, Product product, InstallmentPlan plan, HandoverChoice handover, string idNumber,
        string idCardImageId, string idCardContentType, DateTimeOffset now) =>
        new()
        {
            Customer = customer,
            Product = product,
            Price = plan.Price,
            FlatRate = product.ProductCategory.FlatRate,
            Term = plan.Term,
            DownPayment = plan.DownPayment,
            Installment = plan.Installment,
            TotalPayable = plan.TotalPayable,
            Handover = handover.Method,
            DeliveryAddress = handover.DeliveryAddress,
            DeliveryProvince = handover.DeliveryProvince,
            Branch = handover.Branch,
            IdNumber = idNumber,
            IdCardImageId = idCardImageId,
            IdCardContentType = idCardContentType,
            SubmittedAt = now,
            Status = ApplicationStatus.Reviewing,
        };

    /// <summary>
    /// Records the ID Verification result. A verified Customer is marked ID-verified (whatever their age);
    /// one under 20 fails Eligibility, see <see cref="IdVerificationOutcome.Under20"/>.
    /// </summary>
    public IdVerificationOutcome RecordIdVerification(bool verified, string? fullName, DateOnly? dateOfBirth, DateTimeOffset now)
    {
        if (Status != ApplicationStatus.Reviewing)
        {
            return IdVerificationOutcome.Rejected;
        }

        if (!verified || fullName is null || dateOfBirth is not { } dob)
        {
            Finish(ApplicationStatus.Rejected, ApplicationReason.IdVerificationFailed, now);
            return IdVerificationOutcome.Rejected;
        }

        FullName = fullName;
        DateOfBirth = dob;
        Customer.MarkIdVerified(IdNumber, fullName, dob, now);

        return Eligibility.IsOldEnough(dob, now) ? IdVerificationOutcome.CreditCheckNext : IdVerificationOutcome.Under20;
    }

    /// <summary>The Credit Decision: Approved only if the Credit Check passed and the financed amount is within the Credit Limit.</summary>
    public void RecordCreditCheck(bool passed, decimal creditLimit, DateTimeOffset now)
    {
        if (Status != ApplicationStatus.Reviewing)
        {
            return;
        }

        if (!passed)
        {
            Finish(ApplicationStatus.Rejected, ApplicationReason.CreditCheckFailed, now);
            return;
        }

        CreditLimit = creditLimit;
        if (FinancedAmount > creditLimit)
        {
            Finish(ApplicationStatus.Rejected, ApplicationReason.OverCreditLimit, now);
        }
        else
        {
            Finish(ApplicationStatus.Approved, null, now);
            Offer = Offer.For(this);
        }
    }

    /// <summary>No Credit Decision in time: Rejected as timed out (no 30-day wait).</summary>
    public void TimeOut(DateTimeOffset now)
    {
        if (Status == ApplicationStatus.Reviewing)
        {
            Finish(ApplicationStatus.Rejected, ApplicationReason.TimedOut, now);
        }
    }

    private void Finish(ApplicationStatus status, ApplicationReason? reason, DateTimeOffset now)
    {
        Status = status;
        Reason = reason;
        DecidedAt = now;
    }
}

/// <summary>Delivery (address in the Bangkok Metropolitan Region) or Store Pickup at a Branch.</summary>
public sealed record HandoverChoice(HandoverMethod Method, string? DeliveryAddress, string? DeliveryProvince, Branch? Branch)
{
    public static HandoverChoice Delivery(string address, string province) => new(HandoverMethod.Delivery, address, province, null);

    public static HandoverChoice StorePickup(Branch branch) => new(HandoverMethod.StorePickup, null, null, branch);
}

/// <summary>Delivery is free and only offered within the Bangkok Metropolitan Region.</summary>
public static class BangkokMetropolitanRegion
{
    public static readonly IReadOnlyList<string> Provinces =
        ["กรุงเทพมหานคร", "นนทบุรี", "ปทุมธานี", "สมุทรปราการ", "สมุทรสาคร", "นครปฐม"];

    public static bool Contains(string? province) => province is not null && Provinces.Contains(province.Trim());
}
