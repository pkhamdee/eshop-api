namespace Eshop.Api.ThirdParty;

/// <summary>Credit Check port: pass or fail, plus the Credit Limit when it passes. Tests replace it with a scripted fake.</summary>
internal interface ICreditCheck
{
    Task<CreditCheckResult> CheckAsync(string idNumber, string fullName, DateOnly dateOfBirth, CancellationToken cancellationToken);
}

/// <summary>CreditLimit is the largest amount the Customer may finance (0 when the check failed).</summary>
internal sealed record CreditCheckResult(bool Passed, decimal CreditLimit)
{
    public static CreditCheckResult Failed { get; } = new(false, 0m);

    public static CreditCheckResult PassedWith(decimal creditLimit) => new(true, creditLimit);
}

/// <summary>Credit Check adapter for thirdparty-mock (<c>POST /credit-check/checks</c>).</summary>
internal sealed class ThirdPartyMockCreditCheck(HttpClient http) : ICreditCheck
{
    public async Task<CreditCheckResult> CheckAsync(
        string idNumber, string fullName, DateOnly dateOfBirth, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            "credit-check/checks", new { idNumber, fullName, dateOfBirth }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Body>(cancellationToken)
            ?? throw new InvalidOperationException("Credit Check returned no body.");

        return body.Passed ? CreditCheckResult.PassedWith(body.CreditLimit ?? 0m) : CreditCheckResult.Failed;
    }

    private sealed record Body(bool Passed, decimal? CreditLimit);
}
