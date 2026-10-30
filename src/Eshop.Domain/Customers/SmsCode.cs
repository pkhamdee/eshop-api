using System.Security.Cryptography;
using System.Text;

namespace Eshop.Domain.Customers;

/// <summary>What an SMS code is for. A code only works for its own purpose.</summary>
public enum SmsCodePurpose
{
    SignIn,

    /// <summary>Confirms accepting an Offer (which creates the Contract).</summary>
    OfferAcceptance,
}

/// <summary>Outcome of checking a code the Customer typed against an <see cref="SmsCode"/>.</summary>
public enum SmsCodeCheck
{
    Accepted,
    Invalid,
    Expired,
    Locked,
}

/// <summary>
/// A one-time code sent by SMS to a mobile number. It expires after <see cref="Lifetime"/>, and after
/// <see cref="MaxFailedAttempts"/> wrong guesses it is locked: even the right code is refused.
/// Only a hash of the code is stored.
/// </summary>
public class SmsCode
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public const int MaxFailedAttempts = 5;

    public int Id { get; set; }

    public required string MobileNumber { get; set; }

    public SmsCodePurpose Purpose { get; set; }

    public required string CodeHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public int FailedAttempts { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>Creates a new random 6-digit code; returns the entity and the plain code to send.</summary>
    public static (SmsCode SmsCode, string Code) Issue(string mobileNumber, SmsCodePurpose purpose, DateTimeOffset now)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        var smsCode = new SmsCode
        {
            MobileNumber = mobileNumber,
            Purpose = purpose,
            CodeHash = Hash(code),
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };
        return (smsCode, code);
    }

    /// <summary>Checks <paramref name="code"/>, counting a wrong guess and marking the code used on success.</summary>
    public SmsCodeCheck Check(string code, DateTimeOffset now)
    {
        if (UsedAt is not null)
        {
            return SmsCodeCheck.Invalid;
        }

        if (FailedAttempts >= MaxFailedAttempts)
        {
            return SmsCodeCheck.Locked;
        }

        if (now >= ExpiresAt)
        {
            return SmsCodeCheck.Expired;
        }

        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(code)), Encoding.ASCII.GetBytes(CodeHash)))
        {
            FailedAttempts++;
            return FailedAttempts >= MaxFailedAttempts ? SmsCodeCheck.Locked : SmsCodeCheck.Invalid;
        }

        UsedAt = now;
        return SmsCodeCheck.Accepted;
    }

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
