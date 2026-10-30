using System.Net;
using System.Net.Http.Json;

namespace Eshop.Api.Tests.BackOffice;

public sealed class BackOfficeAccessTests(EshopApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> BackOfficeEndpoints() => new()
    {
        { "GET", "/api/back-office/products" },
        { "POST", "/api/back-office/products" },
        { "PUT", "/api/back-office/products/1" },
        { "POST", "/api/back-office/products/1/retire" },
        { "POST", "/api/back-office/product-photos" },
        { "GET", "/api/back-office/product-categories" },
        { "PUT", "/api/back-office/product-categories/mobile-phone" },
        { "GET", "/api/back-office/branches" },
        { "POST", "/api/back-office/branches" },
        { "PUT", "/api/back-office/branches/bkk-ladprao" },
        { "POST", "/api/back-office/branches/bkk-ladprao/retire" },
        { "GET", "/api/back-office/auctions" },
        { "POST", "/api/back-office/auctions" },
        { "GET", "/api/back-office/auctions/1" },
        { "POST", "/api/back-office/auctions/1/lots" },
        { "PUT", "/api/back-office/lots/1" },
        { "POST", "/api/back-office/lot-photos" },
        { "GET", "/api/back-office/handovers" },
        { "GET", "/api/back-office/handovers/SP-202601-000001" },
        { "GET", "/api/back-office/handovers/SP-202601-000001/id-card-photo" },
        { "POST", "/api/back-office/handovers/SP-202601-000001/stage" },
        { "POST", "/api/back-office/handovers/SP-202601-000001/down-payment" },
        { "POST", "/api/back-office/lots/1/payment" },
        { "POST", "/api/back-office/lots/1/forfeit" },
        { "POST", "/api/back-office/lots/1/re-offer" },
    };

    [Theory]
    [MemberData(nameof(BackOfficeEndpoints))]
    public async Task Anonymous_callers_must_sign_in(string method, string path)
    {
        var response = await api.CreateClient().SendAsync(Request(method, path), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BackOfficeEndpoints))]
    public async Task Customers_are_forbidden(string method, string path)
    {
        var customer = await api.CreateCustomerClientAsync();

        var response = await customer.SendAsync(Request(method, path), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Staff_are_let_in()
    {
        var staff = await api.CreateStaffClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/back-office/products", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/back-office/product-categories", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/back-office/branches", Ct)).StatusCode);
    }

    private static HttpRequestMessage Request(string method, string path) =>
        new(new HttpMethod(method), path)
        {
            Content = method == "GET" ? null : JsonContent.Create(new { }),
        };
}
