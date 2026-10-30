using System.Collections.Concurrent;
using System.Threading.Channels;
using Eshop.Api.Images;
using Eshop.Api.ThirdParty;
using Eshop.Data;
using Eshop.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Api.Applications;

/// <summary>Hands submitted Applications to <see cref="CreditDecisionWorker"/>.</summary>
internal sealed class CreditDecisionQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();

    public void Enqueue(int applicationId) => _channel.Writer.TryWrite(applicationId);

    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Decides Applications in the background: ID Verification (Eligibility age check), then Credit Check, each
/// Application on its own task so a slow third party never holds up others. Every Application gets a
/// deadline of <see cref="Application.DecisionTimeLimit"/> after submitting, measured on the
/// <see cref="TimeProvider"/> clock port (tests move it); missing it means Rejected as timed out.
/// Applications still Reviewing at start-up (e.g. after a restart) are picked up again.
/// </summary>
internal sealed partial class CreditDecisionWorker(
    CreditDecisionQueue queue, IServiceScopeFactory scopes, TimeProvider clock, ILogger<CreditDecisionWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<int, Task> _inFlight = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EshopDbContext>();
            var pending = await db.Applications
                .Where(a => a.Status == ApplicationStatus.Reviewing)
                .Select(a => a.Id)
                .ToListAsync(stoppingToken);
            pending.ForEach(Start);
        }

        await foreach (var id in queue.ReadAllAsync(stoppingToken))
        {
            Start(id);
        }

        void Start(int id) =>
            _inFlight.GetOrAdd(id, key => Task.Run(
                async () =>
                {
                    try
                    {
                        await DecideAsync(key, stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        LogDecisionFailed(ex, key);
                    }
                    finally
                    {
                        _inFlight.TryRemove(key, out _);
                    }
                },
                CancellationToken.None));
    }

    private async Task DecideAsync(int id, CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<EshopDbContext>();
        var application = await db.Applications.Include(a => a.Customer).SingleAsync(a => a.Id == id, stoppingToken);
        if (application.Status != ApplicationStatus.Reviewing)
        {
            return;
        }

        var remaining = application.DecisionDeadline - clock.GetUtcNow();
        using var deadline = new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, stoppingToken);

        try
        {
            await RunChecksAsync(application, services, db, linked.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            application.TimeOut(clock.GetUtcNow());
            await db.SaveChangesAsync(stoppingToken);
        }

        LogDecided(application.Id, application.Status, application.Reason);
    }

    private async Task RunChecksAsync(Application application, IServiceProvider services, EshopDbContext db, CancellationToken ct)
    {
        var images = services.GetRequiredService<IImageStore>();
        var photo = await RetryAsync(() => images.GetAsync(ImageArea.Private, application.IdCardImageId, ct), ct)
            ?? throw new InvalidOperationException($"ID card photo {application.IdCardImageId} is missing.");

        var idVerification = services.GetRequiredService<IIdVerification>();
        var identity = await RetryAsync(
            () => idVerification.VerifyAsync(application.IdNumber, new StoredIdCardPhoto(photo.Content, photo.ContentType), ct), ct);

        var now = clock.GetUtcNow();
        var outcome = application.RecordIdVerification(identity.Verified, identity.FullName, identity.DateOfBirth, now);
        if (outcome == IdVerificationOutcome.Under20)
        {
            // Failing Eligibility leaves no Application: swap it for a refusal in one SaveChanges (one transaction).
            db.EligibilityRefusals.Add(EligibilityRefusal.Under20(application, now));
            db.Applications.Remove(application);
        }

        await db.SaveChangesAsync(ct);
        if (outcome != IdVerificationOutcome.CreditCheckNext)
        {
            return;
        }

        var creditCheck = services.GetRequiredService<ICreditCheck>();
        var credit = await RetryAsync(
            () => creditCheck.CheckAsync(application.IdNumber, application.FullName!, application.DateOfBirth!.Value, ct), ct);

        // Approved comes with its Offer, saved together.
        application.RecordCreditCheck(credit.Passed, credit.CreditLimit, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A third party that errors is asked again until it answers or the deadline passes.</summary>
    private async Task<T> RetryAsync<T>(Func<Task<T>> call, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                return await call();
            }
            catch (HttpRequestException ex)
            {
                LogThirdPartyError(ex);
                await Task.Delay(RetryDelay, clock, ct);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Application {ApplicationId} decided: {Status} {Reason}")]
    private partial void LogDecided(int applicationId, ApplicationStatus status, ApplicationReason? reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Third party call failed; retrying until the decision deadline")]
    private partial void LogThirdPartyError(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Deciding Application {ApplicationId} failed")]
    private partial void LogDecisionFailed(Exception exception, int applicationId);
}
