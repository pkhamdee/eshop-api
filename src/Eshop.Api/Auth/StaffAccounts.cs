using Eshop.Data;
using Eshop.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Api.Auth;

/// <summary>
/// Provisions Staff accounts from configuration (<c>Staff:Accounts:N:Username|Password|DisplayName</c>). In the
/// cluster these come from the per-environment SOPS Secret <c>eshop-api-staff</c>, so no password is ever in
/// git in plain text. Runs at startup: missing accounts are created, and an account whose configured password
/// changed gets the new one (the Secret is the source of truth).
/// </summary>
internal static class StaffAccounts
{
    public static async Task ProvisionAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var configured = configuration.GetSection("Staff:Accounts").GetChildren()
            .Select(a => (Username: a["Username"]?.Trim().ToLowerInvariant(), Password: a["Password"], DisplayName: a["DisplayName"]))
            .Where(a => !string.IsNullOrEmpty(a.Username) && !string.IsNullOrEmpty(a.Password))
            .ToList();
        if (configured.Count == 0)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EshopDbContext>();
        foreach (var (username, password, displayName) in configured)
        {
            var staff = await db.StaffMembers.SingleOrDefaultAsync(s => s.Username == username, cancellationToken);
            if (staff is null)
            {
                staff = new StaffMember { Username = username!, DisplayName = displayName ?? username! };
                staff.SetPassword(password!);
                db.StaffMembers.Add(staff);
            }
            else
            {
                if (!staff.HasPassword(password!))
                {
                    staff.SetPassword(password!);
                }

                staff.DisplayName = displayName ?? staff.DisplayName;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
