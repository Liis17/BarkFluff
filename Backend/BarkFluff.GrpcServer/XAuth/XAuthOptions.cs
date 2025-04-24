namespace BarkFluff.GrpcServer.XAuth;

public class XAuthOptions
{
    public string Secret { get; set; } = null!;
    
    public string Issuer { get; set; } = null!;
    
    public string Audience { get; set; } = null!;
    
    public bool RequireHttpsMetadata { get; set; } = true;
}