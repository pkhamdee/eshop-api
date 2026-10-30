using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Eshop.Api.ThirdParty;

namespace Eshop.Api.Tests.Fakes;

/// <summary>
/// In-memory SMS port: records every message instead of sending it, so tests can read codes
/// the way a Customer reads their phone.
/// </summary>
public sealed partial class FakeSms : ISmsSender
{
    private readonly ConcurrentQueue<SentSms> _sent = new();

    public IEnumerable<SentSms> SentTo(string mobileNumber) => _sent.Where(m => m.MobileNumber == mobileNumber);

    /// <summary>The 6-digit code in the most recent SMS to <paramref name="mobileNumber"/>.</summary>
    public string LatestCodeFor(string mobileNumber)
    {
        var message = SentTo(mobileNumber).LastOrDefault()
            ?? throw new InvalidOperationException($"No SMS was sent to {mobileNumber}.");
        return SixDigits().Match(message.Text) is { Success: true } match
            ? match.Value
            : throw new InvalidOperationException($"No code in SMS '{message.Text}'.");
    }

    Task ISmsSender.SendAsync(string mobileNumber, string text, CancellationToken cancellationToken)
    {
        _sent.Enqueue(new SentSms(mobileNumber, text));
        return Task.CompletedTask;
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}

public sealed record SentSms(string MobileNumber, string Text);
