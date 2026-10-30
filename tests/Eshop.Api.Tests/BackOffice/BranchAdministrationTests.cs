using System.Net;
using System.Net.Http.Json;

namespace Eshop.Api.Tests.BackOffice;

public sealed class BranchAdministrationTests(EshopApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Staff_add_a_Branch_and_Customers_can_pick_it_for_Store_Pickup()
    {
        var staff = await api.CreateStaffClientAsync();
        var code = NewCode();

        var response = await staff.PostAsJsonAsync("/api/back-office/branches", new
        {
            code,
            name = "สาขาบางนา",
            address = "123 ถนนบางนา-ตราด แขวงบางนา เขตบางนา",
            province = "กรุงเทพมหานคร",
        }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var branches = await api.CreateClient().GetFromJsonAsync<List<Branch>>("/api/branches", Ct);
        var added = Assert.Single(branches!, b => b.Code == code);
        Assert.Equal("สาขาบางนา", added.Name);
        Assert.Equal("กรุงเทพมหานคร", added.Province);
    }

    [Fact]
    public async Task Staff_edit_a_Branch()
    {
        var staff = await api.CreateStaffClientAsync();
        var code = await AddBranchAsync(staff);

        var response = await staff.PutAsJsonAsync($"/api/back-office/branches/{code}", new
        {
            name = "สาขาเชียงใหม่ (ย้ายใหม่)",
            address = "99 ถนนนิมมานเหมินท์",
            province = "เชียงใหม่",
        }, Ct);

        response.EnsureSuccessStatusCode();
        var branch = (await api.CreateClient().GetFromJsonAsync<List<Branch>>("/api/branches", Ct))!.Single(b => b.Code == code);
        Assert.Equal("สาขาเชียงใหม่ (ย้ายใหม่)", branch.Name);
        Assert.Equal("99 ถนนนิมมานเหมินท์", branch.Address);
        Assert.Equal("เชียงใหม่", branch.Province);
    }

    [Fact]
    public async Task A_retired_Branch_is_no_longer_offered_but_Staff_still_see_it()
    {
        var staff = await api.CreateStaffClientAsync();
        var code = await AddBranchAsync(staff);

        var retire = await staff.PostAsync($"/api/back-office/branches/{code}/retire", null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
        Assert.DoesNotContain((await api.CreateClient().GetFromJsonAsync<List<Branch>>("/api/branches", Ct))!, b => b.Code == code);
        var backOffice = await staff.GetFromJsonAsync<List<BackOfficeBranch>>("/api/back-office/branches", Ct);
        Assert.True(Assert.Single(backOffice!, b => b.Code == code).IsRetired);

        // Retiring by mistake can be undone.
        Assert.Equal(HttpStatusCode.NoContent, (await staff.PostAsync($"/api/back-office/branches/{code}/reinstate", null, Ct)).StatusCode);
        Assert.Contains((await api.CreateClient().GetFromJsonAsync<List<Branch>>("/api/branches", Ct))!, b => b.Code == code);
    }

    [Fact]
    public async Task Branch_codes_are_unique()
    {
        var staff = await api.CreateStaffClientAsync();

        var response = await staff.PostAsJsonAsync("/api/back-office/branches", new
        {
            code = "bkk-ladprao",
            name = "ซ้ำ",
            address = "ที่อยู่",
            province = "กรุงเทพมหานคร",
        }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("Not A Code", "ชื่อ", "ที่อยู่", "จังหวัด")]
    [InlineData("ok-code", "", "ที่อยู่", "จังหวัด")]
    [InlineData("ok-code", "ชื่อ", "", "จังหวัด")]
    [InlineData("ok-code", "ชื่อ", "ที่อยู่", "")]
    public async Task An_incomplete_Branch_is_refused(string code, string name, string address, string province)
    {
        var staff = await api.CreateStaffClientAsync();

        var response = await staff.PostAsJsonAsync("/api/back-office/branches", new { code, name, address, province }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Editing_or_retiring_an_unknown_Branch_is_not_found()
    {
        var staff = await api.CreateStaffClientAsync();

        var edit = await staff.PutAsJsonAsync("/api/back-office/branches/nowhere-1", new { name = "x", address = "y", province = "z" }, Ct);
        var retire = await staff.PostAsync("/api/back-office/branches/nowhere-1/retire", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, retire.StatusCode);
    }

    private static string NewCode() => $"test-{Guid.NewGuid():N}"[..20];

    private static async Task<string> AddBranchAsync(HttpClient staff)
    {
        var code = NewCode();
        (await staff.PostAsJsonAsync("/api/back-office/branches", new
        {
            code,
            name = "สาขาเชียงใหม่",
            address = "1 ถนนห้วยแก้ว",
            province = "เชียงใหม่",
        }, Ct)).EnsureSuccessStatusCode();
        return code;
    }

    private sealed record Branch(string Code, string Name, string Address, string Province);

    private sealed record BackOfficeBranch(string Code, string Name, bool IsRetired);
}
