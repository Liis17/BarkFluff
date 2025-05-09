namespace BarkFluff.GrpcServer.Settings;

public class RunSettings
{
    public string? Host { get; set; }
    
    public int Port { get; set; }
    
    public int? Http1Port { get; set; }
    
    public TlsSettings? Tls { get; set; }
}