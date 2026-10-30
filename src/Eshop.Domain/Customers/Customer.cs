namespace Eshop.Domain.Customers;

/// <summary>
/// A person who applies for hire purchase, holds Contracts, or bids in an Auction (GLOSSARY: Customer).
/// Created automatically the first time a mobile number signs in with an SMS code.
/// </summary>
public class Customer
{
    public int Id { get; set; }

    /// <summary>10-digit Thai mobile number, e.g. <c>0812345678</c>. Unique.</summary>
    public required string MobileNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>National ID number that passed ID Verification (13 digits), or null.</summary>
    public string? IdNumber { get; set; }

    /// <summary>Name from ID Verification.</summary>
    public string? FullName { get; set; }

    /// <summary>Date of birth from ID Verification.</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>When the Customer last passed ID Verification; null if never (they may not bid in Auctions).</summary>
    public DateTimeOffset? IdVerifiedAt { get; set; }

    public bool IsIdVerified => IdVerifiedAt is not null;

    public void MarkIdVerified(string idNumber, string fullName, DateOnly dateOfBirth, DateTimeOffset now)
    {
        IdNumber = idNumber;
        FullName = fullName;
        DateOfBirth = dateOfBirth;
        IdVerifiedAt = now;
    }
}
