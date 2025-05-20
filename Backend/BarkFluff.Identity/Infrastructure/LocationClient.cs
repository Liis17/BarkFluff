namespace BarkFluff.Identity.Infrastructure;

public class LocationClient
{
    private const string BaseUrl = "http://ip-api.com/json/";
    
    
    public async Task<IpLocation?> GetLocation(string ip)
    {
        return null;
    }
}