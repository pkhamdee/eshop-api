using System.Net.Http.Headers;
using System.Net.Http.Json;
using Eshop.Api.Images;
using Eshop.Api.Tests.Fakes;
using Eshop.Api.ThirdParty;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Eshop.Api.Tests.EshopApiFactory))]

namespace Eshop.Api.Tests;

/// <summary>
/// Seam-1 harness: hosts eshop-api in-process against a real PostgreSQL container.
/// One instance (one container, one migrated database) is shared by every test in the assembly.
/// Ports to the outside world (clock, SMS, ...) are replaced here by test-controlled fakes.
/// </summary>
public sealed class EshopApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    /// <summary>The clock port. Tests move time with <c>Clock.SetUtcNow</c> / <c>Clock.Advance</c>.</summary>
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 5, 3, 0, 0, TimeSpan.Zero));

    /// <summary>The SMS port. Read sign-in codes with <c>Sms.LatestCodeFor(mobileNumber)</c>.</summary>
    public FakeSms Sms { get; } = new();

    /// <summary>The image service port (eshop-node-api).</summary>
    public FakeImageStore Images { get; } = new();

    /// <summary>The ID Verification port, scripted per ID number.</summary>
    public FakeIdVerification IdVerification { get; } = new();

    /// <summary>The Credit Check port, scripted per ID number.</summary>
    public FakeCreditCheck CreditCheck { get; } = new();

    /// <summary>The Staff account the harness provisions from configuration, the same way the cluster does.</summary>
    public const string StaffUsername = "test-staff";

    public const string StaffPassword = "test-staff-password";

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        // Build the host (migrate + seed) once, before test classes run in parallel.
        StartServer();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Eshop", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Auth:SigningKey", "test-signing-key-for-eshop-api-seam-1-tests-only");
        builder.UseSetting("Staff:Accounts:0:Username", StaffUsername);
        builder.UseSetting("Staff:Accounts:0:Password", StaffPassword);
        builder.UseSetting("Staff:Accounts:0:DisplayName", "พนักงานทดสอบ");
        builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
            services.Replace(ServiceDescriptor.Singleton<ISmsSender>(Sms));
            services.Replace(ServiceDescriptor.Singleton<IImageStore>(Images));
            services.Replace(ServiceDescriptor.Singleton<IIdVerification>(IdVerification));
            services.Replace(ServiceDescriptor.Singleton<ICreditCheck>(CreditCheck));
        });
    }

    /// <summary>
    /// Signs in as a Customer through the real SMS-code flow (request code → read it from the fake SMS
    /// → verify) and returns a client that sends the Customer's token. A fresh mobile number (so a new
    /// Customer) is used unless one is given.
    /// </summary>
    public async Task<HttpClient> CreateCustomerClientAsync(string? mobileNumber = null)
    {
        var ct = TestContext.Current.CancellationToken;
        mobileNumber ??= TestData.NewMobileNumber();
        var client = CreateClient();

        (await client.PostAsJsonAsync("/api/auth/sms-code", new { mobileNumber }, ct)).EnsureSuccessStatusCode();
        var code = Sms.LatestCodeFor(mobileNumber);
        var verify = await client.PostAsJsonAsync("/api/auth/sms-code/verify", new { mobileNumber, code }, ct);
        verify.EnsureSuccessStatusCode();
        var session = await verify.Content.ReadFromJsonAsync<TokenResponse>(ct);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);
        return client;
    }

    /// <summary>
    /// Signs in as the harness's Staff account through the real username/password flow and returns a
    /// client that sends the Staff token.
    /// </summary>
    public async Task<HttpClient> CreateStaffClientAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClient();

        var signIn = await client.PostAsJsonAsync(
            "/api/auth/staff/sign-in", new { username = StaffUsername, password = StaffPassword }, ct);
        signIn.EnsureSuccessStatusCode();
        var session = await signIn.Content.ReadFromJsonAsync<TokenResponse>(ct);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);
        return client;
    }

    private sealed record TokenResponse(string Token);

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
