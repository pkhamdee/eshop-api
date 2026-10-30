using Eshop.Api.Applications;
using Eshop.Api.Auctions;
using Eshop.Api.Auth;
using Eshop.Api.BackOffice;
using Eshop.Api.Branches;
using Eshop.Api.Catalog;
using Eshop.Api.Contracts;
using Eshop.Api.Images;
using Eshop.Api.Offers;
using Eshop.Api.ThirdParty;
using Eshop.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs to stdout (read with kubectl logs).
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.IncludeScopes = true;
});

// Clock port: all time-dependent rules read the time from TimeProvider; tests replace it.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddEshopData(
    builder.Configuration.GetConnectionString("Eshop")
        ?? throw new InvalidOperationException("Connection string 'Eshop' is not configured."));

// Third-party ports (SMS, ID Verification, Credit Check) → thirdparty-mock; tests replace them.
builder.Services.AddThirdParties(builder.Configuration);

// Image service port (eshop-node-api internal API); tests replace it.
builder.Services.AddImageService(builder.Configuration);

// Credit Decisions are made in the background; submitting an Application only queues it.
builder.Services.AddSingleton<CreditDecisionQueue>();
builder.Services.AddHostedService<CreditDecisionWorker>();

// eshop-api issues its own Customer and Staff tokens (Auth:SigningKey, per environment).
builder.Services.AddEshopAuth(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<EshopDbContext>(tags: ["ready"]);

builder.Services.AddProblemDetails();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<EshopDbContext>().Database.MigrateAsync();
}

// Staff accounts come from per-environment configuration (SOPS Secret eshop-api-staff in the cluster).
await StaffAccounts.ProvisionAsync(app.Services, app.Configuration, CancellationToken.None);

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

// Probes stay outside /api so the Gateway never exposes them.
app.MapHealthChecks("/healthz/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

var api = app.MapGroup("/api");
api.MapCatalogEndpoints();
api.MapBranchEndpoints();
api.MapAuthEndpoints();
api.MapApplicationEndpoints();
api.MapOfferEndpoints();
api.MapContractEndpoints();
api.MapAuctionEndpoints();
api.MapBidEndpoints();
api.MapBackOfficeEndpoints();

await app.RunAsync();

public partial class Program;
