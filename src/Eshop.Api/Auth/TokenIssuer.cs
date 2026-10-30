using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Eshop.Api.Auth;

/// <summary>Issues eshop-api's own login tokens (HS256 JWTs signed with <c>Auth:SigningKey</c>).</summary>
internal sealed class TokenIssuer(AuthSettings settings, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    public IssuedToken Issue(string role, string subject, TimeSpan lifetime, IEnumerable<Claim> extraClaims)
    {
        var now = clock.GetUtcNow();
        var expiresAt = now + lifetime;
        var claims = new List<Claim>(extraClaims)
        {
            new(EshopClaims.Subject, subject),
            new(EshopClaims.Role, role),
        };

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = AuthSettings.Issuer,
            Audience = AuthSettings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256),
        });

        return new IssuedToken(token, expiresAt, role);
    }
}

internal sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt, string Role);

/// <summary>Token signing settings. The key is per environment (SOPS secret in the cluster).</summary>
internal sealed class AuthSettings
{
    public const string Issuer = "eshop-api";

    public const string Audience = "eshop";

    public AuthSettings(IConfiguration configuration)
    {
        var key = configuration["Auth:SigningKey"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must be set to at least 32 bytes.");
        }

        SigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    }

    public SymmetricSecurityKey SigningKey { get; }
}
