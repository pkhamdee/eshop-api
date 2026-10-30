using System.Net.Http.Json;
using Eshop.Api.Tests.Applications;
using Eshop.Api.Tests.Offers;

namespace Eshop.Api.Tests.Handovers;

/// <summary>
/// What the Staff Handover screens and the Customer's Contract screen do, through the HTTP API only: get a
/// Customer to an accepted Contract, list Handovers, move a Handover's stage, record the Down Payment.
/// </summary>
internal static class HandingOver
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A fresh Customer accepts an Offer for an iPhone 17 (Store Pickup at สาขาลาดพร้าว unless <paramref name="form"/> says otherwise).</summary>
    public static async Task<AcceptedContract> AcceptedContractAsync(
        this EshopApiFactory api, decimal downPayment = 0m, Func<ApplicationForm, ApplicationForm>? form = null)
    {
        var mobile = TestData.NewMobileNumber();
        var customer = await api.CreateCustomerClientAsync(mobile);
        var idNumber = TestData.NewIdNumber();
        api.IdVerification.Verifies(idNumber, "สมชาย รับของ", new DateOnly(1990, 4, 13));
        api.CreditCheck.Passes(idNumber, 50_000m);
        var applicationForm = new ApplicationForm(await customer.IPhone17Async(), idNumber) { DownPayment = downPayment, Term = 6 };
        applicationForm = form?.Invoke(applicationForm) ?? applicationForm;
        var application = await customer.ApplyAndWaitAsync(applicationForm);
        Assert.Equal("approved", application.Status);
        var contract = await customer.AcceptWithSmsCodeAsync(api, application.Offer!.Id, mobile);
        return new AcceptedContract(customer, mobile, idNumber, applicationForm.Photo!, contract);
    }

    public static async Task<List<HandoverView>> HandoversAsync(this HttpClient staff, string query = "") =>
        (await staff.GetFromJsonAsync<List<HandoverView>>("/api/back-office/handovers" + query, Ct))!;

    public static async Task<HandoverView> HandoverAsync(this HttpClient staff, string contractNumber) =>
        (await staff.GetFromJsonAsync<HandoverView>($"/api/back-office/handovers/{contractNumber}", Ct))!;

    public static Task<HttpResponseMessage> MoveToAsync(this HttpClient staff, string contractNumber, string stage) =>
        staff.PostAsJsonAsync($"/api/back-office/handovers/{contractNumber}/stage", new { stage }, Ct);

    public static Task<HttpResponseMessage> RecordDownPaymentAsync(this HttpClient staff, string contractNumber, decimal amount) =>
        staff.PostAsJsonAsync($"/api/back-office/handovers/{contractNumber}/down-payment", new { amount }, Ct);

    /// <summary>Moves through each stage, expecting 200 every time.</summary>
    public static async Task MoveThroughAsync(this HttpClient staff, string contractNumber, params string[] stages)
    {
        foreach (var stage in stages)
        {
            using var response = await staff.MoveToAsync(contractNumber, stage);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Assert.Fail($"Moving to {stage}: expected 200, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
            }
        }
    }
}

internal sealed record AcceptedContract(HttpClient Customer, string MobileNumber, string IdNumber, byte[] IdCardPhoto, ContractView Contract)
{
    public string Number => Contract.Number;
}

internal sealed record HandoverView(
    string ContractNumber,
    string ContractStatus,
    string Stage,
    List<string> NextStages,
    bool Stopped,
    string Method,
    HandoverBranchView? Branch,
    string? DeliveryAddress,
    string? DeliveryProvince,
    HandoverCustomerView Customer,
    ApplicationProductView Product,
    decimal DownPayment,
    decimal? DownPaymentReceived,
    DateTimeOffset? HandedOverAt,
    string IdCardPhotoUrl);

internal sealed record HandoverBranchView(string Code, string Name);

internal sealed record HandoverCustomerView(string FullName, string MobileNumber, string IdNumber);
