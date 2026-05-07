namespace BarkFluff.Identity.Infrastructure;

public static class LocationClientExtensions
{
    private const string Unknown = "-";

    public static async Task<string> GetLocationString(this LocationClient client, string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress))
            return Unknown;

        var location = await client.GetLocation(ipAddress);
        return location is null
            ? Unknown
            : $"{location.Country}, {location.RegionName}, {location.City}";
    }
}
