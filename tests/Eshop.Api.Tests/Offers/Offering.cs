using System.Net.Http.Json;
using Eshop.Api.Tests.Applications;

namespace Eshop.Api.Tests.Offers;

/// <summary>
/// What a Customer's Offer and Contract screens do, through the HTTP API only: get Approved, read the Offer,
/// ask for an SMS code and accept with it, or decline.
/// </summary>
internal static class Offering
{
    private static readonly DateOnly Adult = new(1990, 4, 13);

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Applies for an iPhone 17 with a fresh ID number that passes every check; returns the Approved Application.</summary>
    public static async Task<ApplicationView> ApproveAsync(
        this HttpClient customer, EshopApiFactory api, decimal downPayment = 0m, int term = 12, string? idNumber = null)
    {
        idNumber ??= TestData.NewIdNumber();
        api.IdVerification.Verifies(idNumber, "สมหญิง ยอมรับข้อเสนอ", Adult);
        api.CreditCheck.Passes(idNumber, 50_000m);
        var form = new ApplicationForm(await customer.IPhone17Async(), idNumber) { DownPayment = downPayment, Term = term };
        var application = await customer.ApplyAndWaitAsync(form);
        Assert.Equal("approved", application.Status);
        return application;
    }

    public static async Task<OfferView> GetOfferAsync(this HttpClient customer, int offerId) =>
        (await customer.GetFromJsonAsync<OfferView>($"/api/offers/{offerId}", Ct))!;

    public static Task<HttpResponseMessage> RequestAcceptanceCodeAsync(this HttpClient customer, int offerId) =>
        customer.PostAsync($"/api/offers/{offerId}/sms-code", null, Ct);

    public static Task<HttpResponseMessage> AcceptAsync(this HttpClient customer, int offerId, string code) =>
        customer.PostAsJsonAsync($"/api/offers/{offerId}/accept", new { code }, Ct);

    public static Task<HttpResponseMessage> DeclineAsync(this HttpClient customer, int offerId) =>
        customer.PostAsync($"/api/offers/{offerId}/decline", null, Ct);

    /// <summary>Asks for a code, reads it from the Customer's phone (the fake SMS) and accepts; returns the Contract.</summary>
    public static async Task<ContractView> AcceptWithSmsCodeAsync(
        this HttpClient customer, EshopApiFactory api, int offerId, string mobileNumber)
    {
        (await customer.RequestAcceptanceCodeAsync(offerId)).EnsureSuccessStatusCode();
        using var response = await customer.AcceptAsync(offerId, api.Sms.LatestCodeFor(mobileNumber));
        if (response.StatusCode != System.Net.HttpStatusCode.Created)
        {
            Assert.Fail($"Expected 201, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<ContractView>(Ct))!;
    }

    public static async Task<ContractView> GetContractAsync(this HttpClient customer, string number) =>
        (await customer.GetFromJsonAsync<ContractView>($"/api/contracts/{number}", Ct))!;

    public static async Task<List<ContractSummaryView>> GetContractsAsync(this HttpClient customer) =>
        (await customer.GetFromJsonAsync<List<ContractSummaryView>>("/api/contracts", Ct))!;
}

internal sealed record OfferView(
    int Id,
    string Status,
    DateTimeOffset ExpiresAt,
    ApplicationProductView Product,
    decimal Price,
    decimal DownPayment,
    int Term,
    decimal Installment,
    decimal TotalPayable,
    string? ContractNumber);

internal sealed record ContractView(
    string Number,
    string Status,
    DateTimeOffset AcceptedAt,
    ApplicationProductView Product,
    decimal Price,
    decimal DownPayment,
    int Term,
    decimal Installment,
    decimal TotalPayable,
    string Handover,
    string HandoverStage,
    DateTimeOffset? HandedOverAt,
    List<InstallmentView> Installments,
    DateTimeOffset? CancelledAt,
    List<PaymentView> Payments,
    decimal AmountPaid);

internal sealed record PaymentView(int? InstallmentNumber, decimal Amount, DateTimeOffset PaidAt, string Channel);

internal sealed record InstallmentView(int Number, decimal Amount, DateOnly? DueDate);

internal sealed record ContractSummaryView(string Number, string Status, ApplicationProductView Product, decimal Installment, int Term);
