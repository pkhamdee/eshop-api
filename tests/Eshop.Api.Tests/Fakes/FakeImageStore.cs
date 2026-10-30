using System.Collections.Concurrent;
using Eshop.Api.Images;

namespace Eshop.Api.Tests.Fakes;

/// <summary>In-memory image service: keeps uploaded images so tests can check what was stored.</summary>
public sealed class FakeImageStore : IImageStore
{
    private readonly ConcurrentDictionary<(ImageArea Area, string Id), StoredImage> _images = new();

    /// <summary>The bytes stored under a public image ID, or null.</summary>
    public byte[]? PublicImage(string id) => _images.TryGetValue((ImageArea.Public, id), out var image) ? image.Content : null;

    /// <summary>Whether an image with exactly these bytes was stored in the private area / the public area.</summary>
    public (bool Private, bool Public) WhereStored(byte[] content) =>
        (_images.Any(i => i.Key.Area == ImageArea.Private && i.Value.Content.SequenceEqual(content)),
         _images.Any(i => i.Key.Area == ImageArea.Public && i.Value.Content.SequenceEqual(content)));

    async Task<string> IImageStore.SaveAsync(ImageArea area, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var extension = contentType switch
        {
            "image/png" => "png",
            "image/webp" => "webp",
            "image/svg+xml" => "svg",
            _ => "jpg",
        };
        var id = $"{Guid.NewGuid()}.{extension}";
        _images[(area, id)] = new StoredImage(buffer.ToArray(), contentType);
        return id;
    }

    Task<StoredImage?> IImageStore.GetAsync(ImageArea area, string id, CancellationToken cancellationToken) =>
        Task.FromResult(_images.TryGetValue((area, id), out var image) ? image : null);
}
