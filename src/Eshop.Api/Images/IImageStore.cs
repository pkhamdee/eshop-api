using System.Net;
using System.Net.Http.Headers;

namespace Eshop.Api.Images;

/// <summary>
/// Image service port (eshop-node-api): stores image bytes on the SMB share and hands back an ID.
/// eshop-api stores only that ID. Public images are served to browsers at <c>/images/public/{id}</c>;
/// private ones (ID card photos) only come back through <see cref="GetAsync"/>. Tests replace it with a fake.
/// </summary>
internal interface IImageStore
{
    /// <summary>Stores an image and returns its ID. <paramref name="contentType"/> is image/jpeg, image/png or image/webp.</summary>
    Task<string> SaveAsync(ImageArea area, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>The image's bytes and content type, or null when there is no such image.</summary>
    Task<StoredImage?> GetAsync(ImageArea area, string id, CancellationToken cancellationToken);
}

internal enum ImageArea
{
    Public,
    Private,
}

internal sealed record StoredImage(byte[] Content, string ContentType);

internal static class ImageTypes
{
    /// <summary>
    /// Photo types eshop-api uploads (the image service also takes SVG, used only for the seeded demo photos:
    /// an uploaded SVG could carry script and is served from our own origin, so uploads refuse it).
    /// </summary>
    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    public const long MaxBytes = 10 * 1024 * 1024;

    // Product photos are served by eshop-node-api through the Gateway.
    public static string PublicUrl(string imageId) => "/images/public/" + imageId;
}

/// <summary>Adapter for eshop-node-api's internal API (<c>POST/GET /internal/images/{area}</c>).</summary>
internal sealed class ImageServiceImageStore(HttpClient http) : IImageStore
{
    public async Task<string> SaveAsync(ImageArea area, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var body = new StreamContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await http.PostAsync($"internal/images/{AreaPath(area)}", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedImage>(cancellationToken)
            ?? throw new InvalidOperationException("The image service returned no image ID.");
        return created.Id;
    }

    public async Task<StoredImage?> GetAsync(ImageArea area, string id, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"internal/images/{AreaPath(area)}/{Uri.EscapeDataString(id)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return new StoredImage(
            await response.Content.ReadAsByteArrayAsync(cancellationToken),
            response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }

    private static string AreaPath(ImageArea area) => area == ImageArea.Public ? "public" : "private";

    private sealed record CreatedImage(string Id);
}

internal static class ImageServiceCollectionExtensions
{
    /// <summary>Registers the image service adapter; base URL <c>ImageService:BaseUrl</c> (k8s: <c>http://eshop-node-api/</c>).</summary>
    public static IServiceCollection AddImageService(this IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration["ImageService:BaseUrl"] ?? "http://localhost:8081/";
        services.AddHttpClient<IImageStore, ImageServiceImageStore>(http =>
        {
            http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            http.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
