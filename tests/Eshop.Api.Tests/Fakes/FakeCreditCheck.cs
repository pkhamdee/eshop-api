using System.Collections.Concurrent;
using Eshop.Api.ThirdParty;

namespace Eshop.Api.Tests.Fakes;

/// <summary>Scripted Credit Check port: each test scripts the answer for its own (unique) ID number.</summary>
public sealed class FakeCreditCheck : ICreditCheck
{
    private readonly ConcurrentDictionary<string, Func<CancellationToken, Task<CreditCheckResult>>> _scripts = new();
    private readonly ConcurrentQueue<string> _checked = new();

    public void Passes(string idNumber, decimal creditLimit) =>
        _scripts[idNumber] = _ => Task.FromResult(CreditCheckResult.PassedWith(creditLimit));

    public void Fails(string idNumber) => _scripts[idNumber] = _ => Task.FromResult(CreditCheckResult.Failed);

    /// <summary>Never answers (until the caller gives up), like a third party that hangs.</summary>
    public void NeverAnswers(string idNumber) =>
        _scripts[idNumber] = async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return CreditCheckResult.Failed;
        };

    public bool WasAskedAbout(string idNumber) => _checked.Contains(idNumber);

    Task<CreditCheckResult> ICreditCheck.CheckAsync(
        string idNumber, string fullName, DateOnly dateOfBirth, CancellationToken cancellationToken)
    {
        _checked.Enqueue(idNumber);
        return _scripts.TryGetValue(idNumber, out var script)
            ? script(cancellationToken)
            : throw new InvalidOperationException($"Credit Check is not scripted for {idNumber}.");
    }
}
