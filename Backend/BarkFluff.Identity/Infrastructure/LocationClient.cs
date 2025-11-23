using System.Net.Http;
using System.Text.Json;

namespace BarkFluff.Identity.Infrastructure;

public class LocationClient
{
    private const string BaseUrl = "http://ip-api.com/json/";
    private readonly HttpClient _httpClient;
    
    public LocationClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }
    
    public async Task<IpLocation?> GetLocation(string ip)
    {
        try
        {
            var url = $"{BaseUrl}{ip}?lang=ru";
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            
            var content = await response.Content.ReadAsStringAsync();
            var location = JsonSerializer.Deserialize<IpLocation>(content);
            
            return location;
        }
        catch
        {
            return null;
        }
    }
}