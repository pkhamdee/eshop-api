namespace Eshop.Api.ThirdParty;

/// <summary>
/// ID Verification port: checks that a national ID number and ID card photo belong to a real, living person
/// and returns their name and date of birth. Tests replace it with a scripted fake.
/// </summary>
internal interface IIdVerification
{
    Task<IdVerificationResult> VerifyAsync(string idNumber, StoredIdCardPhoto photo, CancellationToken cancellationToken);
}

internal sealed record StoredIdCardPhoto(byte[] Content, string ContentType);

/// <summary>Verified: FullName and DateOfBirth are set. Not verified: both are null.</summary>
internal sealed record IdVerificationResult(bool Verified, string? FullName, DateOnly? DateOfBirth)
{
    public static IdVerificationResult Failed { get; } = new(false, null, null);

    public static IdVerificationResult Of(string fullName, DateOnly dateOfBirth) => new(true, fullName, dateOfBirth);
}

/// <summary>ID Verification adapter for thirdparty-mock (<c>POST /id-verification/checks</c>).</summary>
internal sealed class ThirdPartyMockIdVerification(HttpClient http) : IIdVerification
{
    public async Task<IdVerificationResult> VerifyAsync(string idNumber, StoredIdCardPhoto photo, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            "id-verification/checks",
            new { idNumber, photo = Convert.ToBase64String(photo.Content), contentType = photo.ContentType },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Body>(cancellationToken)
            ?? throw new InvalidOperationException("ID Verification returned no body.");

        return body is { Verified: true, FullName: { } name, DateOfBirth: { } dob }
            ? IdVerificationResult.Of(name, dob)
            : IdVerificationResult.Failed;
    }

    private sealed record Body(bool Verified, string? FullName, DateOnly? DateOfBirth);
}
