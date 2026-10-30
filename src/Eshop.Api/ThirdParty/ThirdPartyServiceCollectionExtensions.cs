namespace Eshop.Api.ThirdParty;

/// <summary>
/// Registers the real adapters for the third-party ports. All of them talk to thirdparty-mock
/// (<c>ThirdParty:BaseUrl</c>).
/// </summary>
internal static class ThirdPartyServiceCollectionExtensions
{
    public static IServiceCollection AddThirdParties(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = new Uri(EnsureTrailingSlash(configuration["ThirdParty:BaseUrl"] ?? "http://localhost:4000/"));

        services.AddHttpClient<ISmsSender, ThirdPartyMockSmsSender>(http =>
        {
            http.BaseAddress = baseUrl;
            http.Timeout = TimeSpan.FromSeconds(10);
        });

        // No HttpClient time-out: a Credit Decision has its own 60 s deadline (CreditDecisionWorker).
        services.AddHttpClient<IIdVerification, ThirdPartyMockIdVerification>(http =>
        {
            http.BaseAddress = baseUrl;
            http.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddHttpClient<ICreditCheck, ThirdPartyMockCreditCheck>(http =>
        {
            http.BaseAddress = baseUrl;
            http.Timeout = Timeout.InfiniteTimeSpan;
        });

        return services;
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
