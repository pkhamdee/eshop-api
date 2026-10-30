using System.Security.Claims;
using System.Text.RegularExpressions;
using Eshop.Api.ThirdParty;
using Eshop.Data;
using Eshop.Domain.Customers;
using Eshop.Domain.Staff;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Api.Auth;

/// <summary>
/// Sign-in. Customers: request an SMS code, then verify it to get a Customer token (the first sign-in
/// creates the Customer). Staff: username and password. <c>GET /auth/me</c> returns who the token belongs to, for any role.
/// </summary>
internal static partial class AuthEndpoints
{
    public static readonly TimeSpan CustomerTokenLifetime = TimeSpan.FromDays(7);

    /// <summary>A Staff token lasts one working day.</summary>
    public static readonly TimeSpan StaffTokenLifetime = TimeSpan.FromHours(12);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");
        auth.MapPost("/sms-code", RequestSmsCode);
        auth.MapPost("/sms-code/verify", VerifySmsCode);
        auth.MapPost("/staff/sign-in", StaffSignIn);
        auth.MapGet("/me", Me).RequireAuthorization();

        return api;
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> RequestSmsCode(
        SmsCodeRequest request, EshopDbContext db, ISmsSender sms, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!IsMobileNumber(request.MobileNumber))
        {
            return InvalidMobileNumber();
        }

        var (smsCode, code) = SmsCode.Issue(request.MobileNumber, SmsCodePurpose.SignIn, clock.GetUtcNow());
        db.SmsCodes.Add(smsCode);
        await db.SaveChangesAsync(cancellationToken);

        var minutes = (int)SmsCode.Lifetime.TotalMinutes;
        await sms.SendAsync(
            request.MobileNumber,
            $"รหัสเข้าสู่ระบบ eShop ของคุณคือ {code} (ใช้ได้ภายใน {minutes} นาที) ห้ามบอกรหัสนี้กับผู้อื่น",
            cancellationToken);

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> VerifySmsCode(
        SmsCodeVerifyRequest request, EshopDbContext db, TokenIssuer tokens, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!IsMobileNumber(request.MobileNumber))
        {
            return InvalidMobileNumber();
        }

        // Only the most recent code counts: requesting a new code replaces the old one.
        var smsCode = await db.SmsCodes
            .Where(c => c.MobileNumber == request.MobileNumber && c.Purpose == SmsCodePurpose.SignIn)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var now = clock.GetUtcNow();
        var check = smsCode?.Check(request.Code ?? string.Empty, now) ?? SmsCodeCheck.Invalid;
        await db.SaveChangesAsync(cancellationToken);

        if (check != SmsCodeCheck.Accepted)
        {
            return Refusal.SmsCode(check);
        }

        var customer = await db.Customers.SingleOrDefaultAsync(c => c.MobileNumber == request.MobileNumber, cancellationToken);
        if (customer is null)
        {
            customer = new Customer { MobileNumber = request.MobileNumber, CreatedAt = now };
            db.Customers.Add(customer);
            await db.SaveChangesAsync(cancellationToken);
        }

        var token = tokens.Issue(
            Roles.Customer,
            customer.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CustomerTokenLifetime,
            [new Claim(EshopClaims.MobileNumber, customer.MobileNumber)]);

        return TypedResults.Ok(new SessionResponse(token.Token, token.ExpiresAt, token.Role, customer.MobileNumber, null, null));
    }

    private static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> StaffSignIn(
        StaffSignInRequest request, EshopDbContext db, TokenIssuer tokens, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim().ToLowerInvariant() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        var staff = username.Length == 0
            ? null
            : await db.StaffMembers.SingleOrDefaultAsync(s => s.Username == username, cancellationToken);

        if (staff is null)
        {
            // Same cost as a real check, so response time doesn't reveal which usernames exist.
            StaffMember.SpendPasswordCheckTime(password);
            return InvalidCredentials();
        }

        if (!staff.HasPassword(password))
        {
            return InvalidCredentials();
        }

        var token = tokens.Issue(
            Roles.Staff,
            staff.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StaffTokenLifetime,
            [new Claim(EshopClaims.Username, staff.Username), new Claim(EshopClaims.DisplayName, staff.DisplayName)]);

        return TypedResults.Ok(new SessionResponse(token.Token, token.ExpiresAt, token.Role, null, staff.Username, staff.DisplayName));
    }

    private static async Task<MeResponse> Me(ClaimsPrincipal user, EshopDbContext db, CancellationToken cancellationToken)
    {
        var customerId = user.CustomerId();
        var idVerified = customerId is not null
            && await db.Customers.AnyAsync(c => c.Id == customerId && c.IdVerifiedAt != null, cancellationToken);
        return new(
            user.FindFirstValue(EshopClaims.Role) ?? string.Empty,
            user.FindFirstValue(EshopClaims.MobileNumber),
            customerId,
            idVerified,
            user.StaffUsername());
    }

    private static ProblemHttpResult InvalidCredentials() =>
        Refusal.BadRequest("credentials-invalid", "ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง");

    private static bool IsMobileNumber([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value) => value is not null && ThaiMobileNumber().IsMatch(value);

    private static ProblemHttpResult InvalidMobileNumber() =>
        Refusal.BadRequest("mobile-number-invalid", "เบอร์โทรศัพท์มือถือไม่ถูกต้อง");

    [GeneratedRegex(@"^0[6-9][0-9]{8}$")]
    private static partial Regex ThaiMobileNumber();
}

internal sealed record SmsCodeRequest(string? MobileNumber);

internal sealed record SmsCodeVerifyRequest(string? MobileNumber, string? Code);

internal sealed record StaffSignInRequest(string? Username, string? Password);

// MobileNumber: Customers only. Username and DisplayName: Staff only.
internal sealed record SessionResponse(
    string Token, DateTimeOffset ExpiresAt, string Role, string? MobileNumber, string? Username, string? DisplayName);

// IdVerified: the Customer has passed ID Verification (false for Staff). Username: Staff only.
internal sealed record MeResponse(string Role, string? MobileNumber, int? CustomerId, bool IdVerified, string? Username);
