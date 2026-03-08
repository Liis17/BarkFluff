using System.Text.Json.Serialization;

namespace BarkFluff.Identity.Infrastructure;

public class IpLocation
{
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("regionName")]
    public string? RegionName { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("org")]
    public string? ProviderOrganization { get; set; }
}