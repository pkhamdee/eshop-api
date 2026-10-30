namespace Eshop.Api.Tests;

/// <summary>Unique test data, so tests sharing one database never collide.</summary>
public static class TestData
{
    /// <summary>A fresh 10-digit Thai mobile number (09xxxxxxxx).</summary>
    public static string NewMobileNumber() =>
        "09" + Random.Shared.NextInt64(0, 100_000_000).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A fresh 13-digit national ID number, starting with 9 so it never matches thirdparty-mock's examples.</summary>
    public static string NewIdNumber() =>
        "9" + Random.Shared.NextInt64(0, 1_000_000_000_000).ToString("D12", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A small, unique "photo" (bytes are never decoded), so a test can find its own upload.</summary>
    public static byte[] NewPhoto() => [0xFF, 0xD8, 0xFF, .. Guid.NewGuid().ToByteArray()];
}
