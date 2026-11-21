namespace BarkFluff.Identity.Infrastructure;

public static class LocationClientExtensions
{
    public static async Task<string> GetLocationString(this LocationClient client, string? ipAddress,
        string unknown = "-", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return unknown;

        var location = await client.GetLocation(ipAddress, cancellationToken);
        if (location is null)
            return unknown;

        var parts = new[] { location.Country, location.RegionName, location.City };
        var address = string.Join(", ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return address.Length == 0 ? unknown : address;
    }
}
