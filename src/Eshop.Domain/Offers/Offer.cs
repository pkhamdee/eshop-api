using Eshop.Domain.Applications;
using Eshop.Domain.Contracts;
using Eshop.Domain.Customers;

namespace Eshop.Domain.Offers;

public enum OfferStatus
{
    /// <summary>Waiting for the Customer to accept or decline (until <see cref="Offer.ExpiresAt"/>).</summary>
    Open,

    /// <summary>Accepted with an SMS code; a Contract exists.</summary>
    Accepted,

    /// <summary>Declined by the Customer. No 30-day wait: they may apply again at once.</summary>
    Declined,

    /// <summary>Not acted on within <see cref="Offer.Lifetime"/> of approval.</summary>
    Expired,
}

/// <summary>
/// The price, Down Payment, Term and Installment of an Approved Application, which the Customer accepts
/// (with an SMS code) or declines; it expires 7 days after approval (GLOSSARY: Offer). The terms are the
/// Application's, locked when it was submitted. Created in the same transaction as the Approved Credit Decision.
/// </summary>
public class Offer
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public int Id { get; set; }

    public int ApplicationId { get; set; }

    public Application Application { get; set; } = null!;

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Stored status. An Open Offer past <see cref="ExpiresAt"/> is Expired even if not yet stored as such:
    /// always read <see cref="StatusAt"/>.
    /// </summary>
    public OfferStatus Status { get; set; }

    /// <summary>When it was accepted, declined or (once noticed) expired.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>The Offer of an Application that has just been Approved.</summary>
    public static Offer For(Application application)
    {
        if (application.Status != ApplicationStatus.Approved || application.DecidedAt is not { } approvedAt)
        {
            throw new InvalidOperationException("Only an Approved Application has an Offer.");
        }

        return new()
        {
            Application = application,
            CustomerId = application.CustomerId,
            CreatedAt = approvedAt,
            ExpiresAt = approvedAt + Lifetime,
            Status = OfferStatus.Open,
        };
    }

    public OfferStatus StatusAt(DateTimeOffset now) =>
        Status == OfferStatus.Open && now >= ExpiresAt ? OfferStatus.Expired : Status;

    public bool IsOpenAt(DateTimeOffset now) => StatusAt(now) == OfferStatus.Open;

    /// <summary>
    /// Accepts the open Offer, creating the Contract numbered <paramref name="number"/>. The caller has checked
    /// the Customer's SMS code and saves the Contract with its outbox entry in one transaction.
    /// </summary>
    public Contract Accept(ContractNumber number, DateTimeOffset now)
    {
        EnsureOpen(now);
        Status = OfferStatus.Accepted;
        ClosedAt = now;
        return Contract.From(this, number, now);
    }

    public void Decline(DateTimeOffset now)
    {
        EnsureOpen(now);
        Status = OfferStatus.Declined;
        ClosedAt = now;
    }

    /// <summary>Stores the Expired status once it has been noticed (see <see cref="StatusAt"/>).</summary>
    public void NoteExpiry(DateTimeOffset now)
    {
        if (StatusAt(now) == OfferStatus.Expired && Status == OfferStatus.Open)
        {
            Status = OfferStatus.Expired;
            ClosedAt = ExpiresAt;
        }
    }

    private void EnsureOpen(DateTimeOffset now)
    {
        if (!IsOpenAt(now))
        {
            throw new InvalidOperationException($"Offer {Id} is {StatusAt(now)}, not Open.");
        }
    }
}
