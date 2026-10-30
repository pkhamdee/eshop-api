using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Eshop.Domain.Staff;

/// <summary>
/// An eShop Co. employee's back-office account (GLOSSARY: Staff). Staff sign in with a username and
/// password; only a salted PBKDF2 hash of the password is stored. Accounts are provisioned from
/// per-environment configuration (a SOPS-encrypted Secret in the cluster), not created in the app.
/// </summary>
public class StaffMember
{
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Scheme = "pbkdf2-sha256";

    public int Id { get; set; }

    /// <summary>Unique, lower-case sign-in name, e.g. <c>staff</c>.</summary>
    public required string Username { get; set; }

    /// <summary>Thai display name shown in the back office.</summary>
    public required string DisplayName { get; set; }

    /// <summary><c>pbkdf2-sha256$iterations$salt$hash</c> (Base64 salt and hash).</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    public void SetPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        PasswordHash = string.Join('$', Scheme, Iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>True when <paramref name="password"/> is this account's password (constant-time comparison).</summary>
    public bool HasPassword(string password)
    {
        var parts = PasswordHash.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>A password check that costs the same as a real one, for unknown usernames (no timing oracle).</summary>
    public static void SpendPasswordCheckTime(string password) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), new byte[SaltBytes], Iterations, HashAlgorithmName.SHA256, HashBytes);
}
