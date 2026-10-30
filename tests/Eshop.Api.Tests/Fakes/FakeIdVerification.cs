using System.Collections.Concurrent;
using Eshop.Api.ThirdParty;

namespace Eshop.Api.Tests.Fakes;

/// <summary>
/// Scripted ID Verification port: each test scripts the answer for its own (unique) ID number.
/// An unscripted ID number throws, so a forgotten script shows up as a stuck Application.
/// </summary>
public sealed class FakeIdVerification : IIdVerification
{
    private readonly ConcurrentDictionary<string, Func<CancellationToken, Task<IdVerificationResult>>> _scripts = new();
    private readonly ConcurrentQueue<(string IdNumber, byte[] Photo)> _calls = new();

    public void Verifies(string idNumber, string fullName, DateOnly dateOfBirth) =>
        _scripts[idNumber] = _ => Task.FromResult(IdVerificationResult.Of(fullName, dateOfBirth));

    public void Fails(string idNumber) => _scripts[idNumber] = _ => Task.FromResult(IdVerificationResult.Failed);

    /// <summary>Never answers (until the caller gives up), like a third party that hangs.</summary>
    public void NeverAnswers(string idNumber) =>
        _scripts[idNumber] = async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return IdVerificationResult.Failed;
        };

    /// <summary>The ID card photos sent with checks of this ID number.</summary>
    public IEnumerable<byte[]> PhotosCheckedFor(string idNumber) =>
        _calls.Where(c => c.IdNumber == idNumber).Select(c => c.Photo);

    Task<IdVerificationResult> IIdVerification.VerifyAsync(
        string idNumber, StoredIdCardPhoto photo, CancellationToken cancellationToken)
    {
        _calls.Enqueue((idNumber, photo.Content));
        return _scripts.TryGetValue(idNumber, out var script)
            ? script(cancellationToken)
            : throw new InvalidOperationException($"ID Verification is not scripted for {idNumber}.");
    }
}
