using System.Net;
using Eshop.Api.Tests.Applications;
using Eshop.Api.Tests.Offers;

namespace Eshop.Api.Tests.Handovers;

// Staff hand over the goods of accepted Contracts: Preparing → Ready for pickup (Store Pickup) or Out for
// delivery (Delivery) → Handed over, recording any Down Payment first.
public sealed class HandoverTests(EshopApiFactory api)
{
    [Fact]
    public async Task Staff_see_accepted_Contracts_awaiting_Handover_filtered_by_Branch_and_method()
    {
        var pickup = await api.AcceptedContractAsync(form: f => f with { BranchCode = "bkk-bangna" });
        var delivery = await api.AcceptedContractAsync(form: f => f with
        {
            Handover = "delivery",
            BranchCode = null,
            DeliveryAddress = "99/1 ซอยงามวงศ์วาน",
            DeliveryProvince = "นนทบุรี",
        });
        var staff = await api.CreateStaffClientAsync();

        var all = (await staff.HandoversAsync()).Select(h => h.ContractNumber).ToList();
        Assert.Contains(pickup.Number, all);
        Assert.Contains(delivery.Number, all);

        var atBangna = await staff.HandoversAsync("?branch=bkk-bangna");
        var listed = Assert.Single(atBangna, h => h.ContractNumber == pickup.Number);
        Assert.All(atBangna, h => Assert.Equal(("store-pickup", "bkk-bangna"), (h.Method, h.Branch?.Code)));
        Assert.Equal(("preparing", "สมชาย รับของ", pickup.MobileNumber, pickup.IdNumber, "iPhone 17 128GB"),
            (listed.Stage, listed.Customer.FullName, listed.Customer.MobileNumber, listed.Customer.IdNumber, listed.Product.Name));
        Assert.Equal(["ready-for-pickup"], listed.NextStages);

        var deliveries = await staff.HandoversAsync("?method=delivery");
        var listedDelivery = Assert.Single(deliveries, h => h.ContractNumber == delivery.Number);
        Assert.All(deliveries, h => Assert.Equal("delivery", h.Method));
        Assert.Equal(("99/1 ซอยงามวงศ์วาน", "นนทบุรี", (HandoverBranchView?)null), (listedDelivery.DeliveryAddress, listedDelivery.DeliveryProvince, listedDelivery.Branch));
        Assert.Equal(["out-for-delivery"], listedDelivery.NextStages);
        Assert.DoesNotContain(await staff.HandoversAsync("?method=store-pickup"), h => h.ContractNumber == delivery.Number);
    }

    [Fact]
    public async Task A_Store_Pickup_goes_Preparing_then_Ready_for_pickup_then_Handed_over_and_leaves_the_list()
    {
        var accepted = await api.AcceptedContractAsync();
        var staff = await api.CreateStaffClientAsync();

        await staff.MoveThroughAsync(accepted.Number, "ready-for-pickup");
        Assert.Equal(("ready-for-pickup", "handed-over"), Stage(await staff.HandoverAsync(accepted.Number)));
        await staff.MoveThroughAsync(accepted.Number, "handed-over");

        var handedOver = await staff.HandoverAsync(accepted.Number);
        Assert.Equal(("handed-over", ""), Stage(handedOver));
        Assert.NotNull(handedOver.HandedOverAt);
        Assert.DoesNotContain(await staff.HandoversAsync(), h => h.ContractNumber == accepted.Number);
    }

    [Fact]
    public async Task A_Delivery_goes_Preparing_then_Out_for_delivery_then_Handed_over()
    {
        var accepted = await api.AcceptedContractAsync(form: f => f with
        {
            Handover = "delivery",
            BranchCode = null,
            DeliveryAddress = "1 ถนนพระราม 4",
            DeliveryProvince = "กรุงเทพมหานคร",
        });
        var staff = await api.CreateStaffClientAsync();

        using var pickupStage = await staff.MoveToAsync(accepted.Number, "ready-for-pickup");
        Assert.Equal(HttpStatusCode.Conflict, pickupStage.StatusCode);
        Assert.Equal("stage-not-allowed", (await pickupStage.ProblemAsync()).Error);

        await staff.MoveThroughAsync(accepted.Number, "out-for-delivery", "handed-over");
        Assert.Equal("handed-over", (await staff.HandoverAsync(accepted.Number)).Stage);
    }

    [Theory]
    [InlineData(new string[0], "handed-over")] // skipping Ready for pickup
    [InlineData(new[] { "ready-for-pickup" }, "ready-for-pickup")] // same stage again
    [InlineData(new[] { "ready-for-pickup", "handed-over" }, "ready-for-pickup")] // going back
    public async Task Stages_are_never_skipped_repeated_or_reversed(string[] done, string next)
    {
        var accepted = await api.AcceptedContractAsync();
        var staff = await api.CreateStaffClientAsync();
        await staff.MoveThroughAsync(accepted.Number, done);
        var before = await staff.HandoverAsync(accepted.Number);

        using var response = await staff.MoveToAsync(accepted.Number, next);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("stage-not-allowed", (await response.ProblemAsync()).Error);
        Assert.Equal(before.Stage, (await staff.HandoverAsync(accepted.Number)).Stage);
    }

    [Fact]
    public async Task An_unknown_stage_or_Contract_is_refused()
    {
        var accepted = await api.AcceptedContractAsync();
        var staff = await api.CreateStaffClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await staff.MoveToAsync(accepted.Number, "preparing")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.MoveToAsync("SP-209901-999999", "ready-for-pickup")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync("/api/back-office/handovers/SP-209901-999999", HandingOver.Ct)).StatusCode);
    }

    [Fact]
    public async Task With_a_Down_Payment_the_goods_are_handed_over_only_after_Staff_record_receiving_it()
    {
        var accepted = await api.AcceptedContractAsync(downPayment: 3_000m);
        var staff = await api.CreateStaffClientAsync();
        await staff.MoveThroughAsync(accepted.Number, "ready-for-pickup");

        using var tooEarly = await staff.MoveToAsync(accepted.Number, "handed-over");
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal("down-payment-not-received", (await tooEarly.ProblemAsync()).Error);

        using var wrongAmount = await staff.RecordDownPaymentAsync(accepted.Number, 2_500m);
        Assert.Equal(HttpStatusCode.BadRequest, wrongAmount.StatusCode);

        using var recorded = await staff.RecordDownPaymentAsync(accepted.Number, 3_000m);
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        Assert.Equal(3_000m, (await staff.HandoverAsync(accepted.Number)).DownPaymentReceived);

        using var again = await staff.RecordDownPaymentAsync(accepted.Number, 3_000m);
        Assert.Equal("down-payment-already-received", (await again.ProblemAsync()).Error);

        await staff.MoveThroughAsync(accepted.Number, "handed-over");
    }

    [Fact]
    public async Task Without_a_Down_Payment_there_is_nothing_to_record()
    {
        var accepted = await api.AcceptedContractAsync(downPayment: 0m);
        var staff = await api.CreateStaffClientAsync();

        using var response = await staff.RecordDownPaymentAsync(accepted.Number, 0m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no-down-payment", (await response.ProblemAsync()).Error);
        await staff.MoveThroughAsync(accepted.Number, "ready-for-pickup", "handed-over");
    }

    [Fact]
    public async Task Staff_can_view_the_Customers_private_ID_card_photo()
    {
        var accepted = await api.AcceptedContractAsync();
        var staff = await api.CreateStaffClientAsync();
        var handover = await staff.HandoverAsync(accepted.Number);

        using var response = await staff.GetAsync(handover.IdCardPhotoUrl, HandingOver.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(accepted.IdCardPhoto, await response.Content.ReadAsByteArrayAsync(HandingOver.Ct));
        Assert.Equal((true, false), api.Images.WhereStored(accepted.IdCardPhoto)); // private area only, no public link
    }

    [Fact]
    public async Task Customers_and_anonymous_callers_cannot_view_an_ID_card_photo()
    {
        var accepted = await api.AcceptedContractAsync();
        var path = $"/api/back-office/handovers/{accepted.Number}/id-card-photo";

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync(path, HandingOver.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accepted.Customer.GetAsync(path, HandingOver.Ct)).StatusCode);
    }

    [Fact]
    public async Task The_Customer_follows_the_Handover_stage_on_their_Contract()
    {
        var accepted = await api.AcceptedContractAsync();
        var staff = await api.CreateStaffClientAsync();
        Assert.Equal("preparing", accepted.Contract.HandoverStage);

        await staff.MoveThroughAsync(accepted.Number, "ready-for-pickup");
        Assert.Equal("ready-for-pickup", (await accepted.Customer.GetContractAsync(accepted.Number)).HandoverStage);

        await staff.MoveThroughAsync(accepted.Number, "handed-over");
        Assert.Equal("handed-over", (await accepted.Customer.GetContractAsync(accepted.Number)).HandoverStage);
    }

    [Fact]
    public async Task On_Handover_the_first_Installment_falls_due_one_month_later_and_each_month_after()
    {
        var accepted = await api.AcceptedContractAsync();
        var staff = await api.CreateStaffClientAsync();
        Assert.All(accepted.Contract.Installments, i => Assert.Null(i.DueDate));

        await staff.MoveThroughAsync(accepted.Number, "ready-for-pickup", "handed-over");

        var contract = await accepted.Customer.GetContractAsync(accepted.Number);
        var handedOverOn = Bangkok.Today(contract.HandedOverAt!.Value);
        Assert.Equal(
            [handedOverOn.AddMonths(1), handedOverOn.AddMonths(2), handedOverOn.AddMonths(3), handedOverOn.AddMonths(4), handedOverOn.AddMonths(5), handedOverOn.AddMonths(6)],
            contract.Installments.Select(i => i.DueDate!.Value));
    }

    private static (string, string) Stage(HandoverView h) => (h.Stage, string.Join(",", h.NextStages));
}
