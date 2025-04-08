namespace BarkFluff.GrpcServer.Settings;

public class RunSettings
{
    public int Port { get; set; }
    
    public TlsSettings? Tls { get; set; }
}