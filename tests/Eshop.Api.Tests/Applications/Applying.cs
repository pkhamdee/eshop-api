using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Eshop.Api.Tests.Applications;

/// <summary>
/// What a Customer's screen does: submit an Application as multipart form data (fields + one ID card photo)
/// and check back until the Credit Decision arrives. Only the HTTP API is used.
/// </summary>
internal static class Applying
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<int> ProductIdAsync(this HttpClient client, string category, string name)
    {
        var products = await client.GetFromJsonAsync<List<ListedProduct>>($"/api/product-categories/{category}/products", Ct);
        return products!.Single(p => p.Name == name).Id;
    }

    public static Task<int> IPhone17Async(this HttpClient client) => client.ProductIdAsync("mobile-phone", "iPhone 17 128GB");

    public static Task<int> IPadA16Async(this HttpClient client) => client.ProductIdAsync("ipad", "iPad (A16) Wi-Fi 128GB");

    public static Task<HttpResponseMessage> SubmitAsync(this HttpClient client, ApplicationForm form)
    {
        var content = new MultipartFormDataContent();
        void Field(string name, object? value)
        {
            if (value is not null)
            {
                content.Add(new StringContent(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!), name);
            }
        }

        Field("productId", form.ProductId);
        Field("term", form.Term);
        Field("downPayment", form.DownPayment);
        Field("idNumber", form.IdNumber);
        Field("handover", form.Handover);
        Field("branchCode", form.BranchCode);
        Field("deliveryAddress", form.DeliveryAddress);
        Field("deliveryProvince", form.DeliveryProvince);
        if (form.Photo is not null)
        {
            var photo = new ByteArrayContent(form.Photo);
            photo.Headers.ContentType = new MediaTypeHeaderValue(form.PhotoContentType);
            content.Add(photo, "idCardPhoto", "id-card.jpg");
        }

        return client.PostAsync("/api/applications", content, Ct);
    }

    /// <summary>Submits and expects 202 Accepted; returns the Application as first shown ("reviewing").</summary>
    public static async Task<ApplicationView> SubmitAcceptedAsync(this HttpClient client, ApplicationForm form)
    {
        using var response = await client.SubmitAsync(form);
        if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
        {
            Assert.Fail($"Expected 202, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<ApplicationView>(Ct))!;
    }

    public static async Task<ApplicationView> GetApplicationAsync(this HttpClient client, int id) =>
        (await client.GetFromJsonAsync<ApplicationView>($"/api/applications/{id}", Ct))!;

    /// <summary>Checks back (like the "กำลังพิจารณา" screen) until the Application is no longer being reviewed.</summary>
    public static async Task<ApplicationView> WaitForDecisionAsync(this HttpClient client, int id)
    {
        var giveUpAt = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var application = await client.GetApplicationAsync(id);
            if (application.Status != "reviewing" || DateTime.UtcNow > giveUpAt)
            {
                return application;
            }

            await Task.Delay(25, Ct);
        }
    }

    /// <summary>Like <see cref="WaitForDecisionAsync"/> but returns the raw response (e.g. 410 when found under 20).</summary>
    public static async Task<HttpResponseMessage> WaitWhileReviewingAsync(this HttpClient client, int id)
    {
        var giveUpAt = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var response = await client.GetAsync($"/api/applications/{id}", Ct);
            if (!response.IsSuccessStatusCode
                || (await response.Content.ReadFromJsonAsync<ApplicationView>(Ct))!.Status != "reviewing"
                || DateTime.UtcNow > giveUpAt)
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(25, Ct);
        }
    }

    public static async Task<ApplicationView> ApplyAndWaitAsync(this HttpClient client, ApplicationForm form) =>
        await client.WaitForDecisionAsync((await client.SubmitAcceptedAsync(form)).Id);

    public static async Task<EligibilityView> EligibilityAsync(this HttpClient client) =>
        (await client.GetFromJsonAsync<EligibilityView>("/api/eligibility", Ct))!;

    public static async Task<MeView> MeAsync(this HttpClient client) =>
        (await client.GetFromJsonAsync<MeView>("/api/auth/me", Ct))!;

    /// <summary>Reads a refusal's machine-readable reason (ProblemDetails <c>error</c>).</summary>
    public static async Task<Problem> ProblemAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Problem>(Ct))!;

    private sealed record ListedProduct(int Id, string Name);
}

/// <summary>A Store Pickup Application at สาขาลาดพร้าว, Term 12, no Down Payment, unless changed.</summary>
internal sealed record ApplicationForm(int ProductId, string IdNumber)
{
    public int? Term { get; init; } = 12;

    public decimal? DownPayment { get; init; } = 0m;

    public string? Handover { get; init; } = "store-pickup";

    public string? BranchCode { get; init; } = "bkk-ladprao";

    public string? DeliveryAddress { get; init; }

    public string? DeliveryProvince { get; init; }

    public byte[]? Photo { get; init; } = TestData.NewPhoto();

    public string PhotoContentType { get; init; } = "image/jpeg";
}

internal sealed record ApplicationView(
    int Id,
    string Status,
    string? Reason,
    DateTimeOffset? ReapplyFrom,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DecidedAt,
    ApplicationProductView Product,
    decimal Price,
    decimal DownPayment,
    int Term,
    decimal Installment,
    decimal TotalPayable,
    string Handover,
    string? DeliveryAddress,
    string? DeliveryProvince,
    ApplicationBranchView? Branch,
    string? FullName,
    DateOnly? DateOfBirth,
    decimal? CreditLimit)
{
    /// <summary>The Offer of an Approved Application (#16).</summary>
    public ApplicationOfferView? Offer { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object>? Other { get; init; }
}

internal sealed record ApplicationOfferView(int Id, string Status, DateTimeOffset ExpiresAt);

internal sealed record ApplicationProductView(int Id, string Name);

internal sealed record ApplicationBranchView(string Code, string Name);

internal sealed record EligibilityView(bool Eligible, string? Reason, DateTimeOffset? ReapplyFrom);

internal sealed record MeView(int? CustomerId, bool IdVerified);

internal sealed record Problem(string? Error, string? Title, DateTimeOffset? ReapplyFrom);
