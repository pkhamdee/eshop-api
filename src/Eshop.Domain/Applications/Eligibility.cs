namespace Eshop.Domain.Applications;

/// <summary>Why a Customer can't submit an Application right now.</summary>
public enum IneligibleReason
{
    /// <summary>The Customer holds an active (or overdue) Contract.</summary>
    ActiveContract,

    /// <summary>
    /// An Offer is still open (Approved, not yet accepted, declined or expired). Accepting it would create a
    /// Contract, so a second Application could only end in a second active Contract; the Customer declines
    /// the Offer (or lets it expire) to apply for something else.
    /// </summary>
    OfferOpen,

    /// <summary>An earlier Application is still being reviewed.</summary>
    ApplicationInProgress,

    /// <summary>Within 30 days of a Rejected Application.</summary>
    RecentlyRejected,

    /// <summary>Under 20, known once ID Verification has answered (see <see cref="EligibilityRefusal"/>).</summary>
    Under20,
}

/// <summary>
/// The rules a Customer must meet before an Application can be submitted (GLOSSARY: Eligibility): aged 20 or
/// over (known only after ID Verification, see <see cref="IsOldEnough"/>), no active Contract, and not within
/// 30 days of a Rejected Application. A time-out Rejection does not count. An open Offer also blocks a new
/// Application (see <see cref="IneligibleReason.OfferOpen"/>).
/// </summary>
public sealed record Eligibility(bool IsEligible, IneligibleReason? Reason, DateTimeOffset? ReapplyFrom, int? OfferId = null)
{
    public const int MinimumAge = 20;

    public static Eligibility Eligible { get; } = new(true, null, null);

    /// <summary>
    /// Checks the rules that are known before ID Verification, from the Customer's earlier Applications (and
    /// any made with the same ID number), with their Offers loaded, and whether they hold an active Contract.
    /// </summary>
    public static Eligibility Check(IEnumerable<Application> earlierApplications, bool hasActiveContract, DateTimeOffset now)
    {
        if (hasActiveContract)
        {
            return new(false, IneligibleReason.ActiveContract, null);
        }

        var applications = earlierApplications.ToList();

        if (applications.Select(a => a.Offer).FirstOrDefault(o => o is not null && o.IsOpenAt(now)) is { } open)
        {
            return new(false, IneligibleReason.OfferOpen, null, open.Id);
        }

        if (applications.Any(a => a.Status == ApplicationStatus.Reviewing))
        {
            return new(false, IneligibleReason.ApplicationInProgress, null);
        }

        var reapplyFrom = applications.Select(a => a.ReapplyFrom).Max();
        return reapplyFrom > now ? new(false, IneligibleReason.RecentlyRejected, reapplyFrom) : Eligible;
    }

    /// <summary>Aged 20 or over today (Asia/Bangkok): a 20th birthday today counts.</summary>
    public static bool IsOldEnough(DateOnly dateOfBirth, DateTimeOffset now)
    {
        return dateOfBirth.AddYears(MinimumAge) <= Bangkok.Today(now);
    }
}

/// <summary>
/// A submission that failed Eligibility only once ID Verification answered (the Customer is under 20). Failing
/// Eligibility creates no Application and no Credit Decision, so the submitted Application is deleted and this
/// record takes its place: it tells the Customer polling <see cref="SubmissionId"/> (the id the submission was
/// given) that they are not eligible. It starts no 30-day wait.
/// </summary>
public class EligibilityRefusal
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    /// <summary>The id the deleted submission had (what the Customer's screen polls). Not a foreign key.</summary>
    public int SubmissionId { get; set; }

    public required string IdNumber { get; set; }

    public IneligibleReason Reason { get; set; }

    public DateTimeOffset RefusedAt { get; set; }

    public static EligibilityRefusal Under20(Application submission, DateTimeOffset now) =>
        new()
        {
            CustomerId = submission.CustomerId,
            SubmissionId = submission.Id,
            IdNumber = submission.IdNumber,
            Reason = IneligibleReason.Under20,
            RefusedAt = now,
        };
}
