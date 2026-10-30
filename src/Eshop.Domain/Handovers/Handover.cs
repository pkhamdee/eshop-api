using Eshop.Domain.Applications;
using Eshop.Domain.Contracts;

namespace Eshop.Domain.Handovers;

public enum HandoverStage
{
    Preparing,

    /// <summary>Store Pickup: the goods wait at the Branch.</summary>
    ReadyForPickup,

    /// <summary>Delivery: the goods are on their way to the Customer's address.</summary>
    OutForDelivery,

    HandedOver,
}

/// <summary>Why Staff can't do what they asked with a Handover.</summary>
public enum HandoverRefusal
{
    None,

    /// <summary>Not the next stage: stages are never skipped and never go back.</summary>
    StageNotAllowed,

    /// <summary>The Contract has a Down Payment that hasn't been recorded yet.</summary>
    DownPaymentNotReceived,

    /// <summary>The Contract was cancelled in the Enterprise System: its Handover stops.</summary>
    Stopped,

    /// <summary>The Contract has no Down Payment to record.</summary>
    NoDownPayment,

    DownPaymentAlreadyReceived,

    /// <summary>The amount isn't the Contract's Down Payment.</summary>
    DownPaymentAmountWrong,
}

/// <summary>
/// The Handover of a Contract's Product (GLOSSARY: Handover), by Delivery or Store Pickup. It is eShop's own
/// operational record, kept beside the Contract rather than on it: eShop never edits a Contract once pushed
/// (ADR-0001, ADR-0004), while Staff move the Handover along for as long as it takes. Created with the Contract.
/// The first Installment falls due one month after the Handover date.
/// </summary>
public class Handover
{
    public int Id { get; set; }

    public int ContractId { get; set; }

    public Contract Contract { get; set; } = null!;

    public HandoverStage Stage { get; set; }

    public DateTimeOffset StageChangedAt { get; set; }

    /// <summary>Username of the Staff member who last moved the stage (null while Preparing).</summary>
    public string? StageChangedBy { get; set; }

    /// <summary>The Down Payment Staff received (always the Contract's Down Payment), or null.</summary>
    public decimal? DownPaymentReceived { get; set; }

    public DateTimeOffset? DownPaymentReceivedAt { get; set; }

    public string? DownPaymentReceivedBy { get; set; }

    public DateTimeOffset? HandedOverAt { get; set; }

    public static Handover For(Contract contract) =>
        new() { Contract = contract, Stage = HandoverStage.Preparing, StageChangedAt = contract.AcceptedAt };

    /// <summary>The Handover date (Asia/Bangkok), from which Installment due dates run.</summary>
    public DateOnly? HandedOverOn => HandedOverAt is { } at ? Bangkok.Today(at) : null;

    /// <summary>A Contract cancelled in the Enterprise System stops a Handover that hasn't happened (needs <see cref="Contract"/>).</summary>
    public bool IsStopped => Contract.Status == ContractStatus.Cancelled && Stage != HandoverStage.HandedOver;

    /// <summary>Needs <see cref="Contract"/> and its Application.</summary>
    public bool DownPaymentOutstanding => Contract.DownPayment > 0 && DownPaymentReceived is null;

    /// <summary>The stage Staff may move to next (none once handed over or stopped). Needs <see cref="Contract"/> and its Application.</summary>
    public HandoverStage? NextStage =>
        IsStopped ? null : Stage switch
        {
            HandoverStage.Preparing => Contract.Application.Handover == HandoverMethod.Delivery
                ? HandoverStage.OutForDelivery
                : HandoverStage.ReadyForPickup,
            HandoverStage.ReadyForPickup or HandoverStage.OutForDelivery => HandoverStage.HandedOver,
            _ => null,
        };

    public HandoverRefusal MoveTo(HandoverStage stage, string staff, DateTimeOffset now)
    {
        if (IsStopped)
        {
            return HandoverRefusal.Stopped;
        }

        if (stage != NextStage)
        {
            return HandoverRefusal.StageNotAllowed;
        }

        if (stage == HandoverStage.HandedOver)
        {
            if (DownPaymentOutstanding)
            {
                return HandoverRefusal.DownPaymentNotReceived;
            }

            HandedOverAt = now;
        }

        Stage = stage;
        StageChangedAt = now;
        StageChangedBy = staff;
        return HandoverRefusal.None;
    }

    /// <summary>Staff record the Contract's Down Payment, received before the goods are handed over.</summary>
    public HandoverRefusal RecordDownPayment(decimal amount, string staff, DateTimeOffset now)
    {
        if (IsStopped)
        {
            return HandoverRefusal.Stopped;
        }

        if (Contract.DownPayment <= 0)
        {
            return HandoverRefusal.NoDownPayment;
        }

        if (DownPaymentReceived is not null)
        {
            return HandoverRefusal.DownPaymentAlreadyReceived;
        }

        if (amount != Contract.DownPayment)
        {
            return HandoverRefusal.DownPaymentAmountWrong;
        }

        DownPaymentReceived = amount;
        DownPaymentReceivedAt = now;
        DownPaymentReceivedBy = staff;
        return HandoverRefusal.None;
    }
}
