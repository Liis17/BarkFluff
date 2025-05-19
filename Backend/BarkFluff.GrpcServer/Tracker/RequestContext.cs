namespace BarkFluff.GrpcServer.Tracker;

public class RequestContext
{
    public string? OperationSystem { get; set; }
    
    public string? IpAddress { get; set; }
    
    public string? DeviceName { get; set; }
    
    public string? AppName { get; set; }
    
    public string? AppVersion { get; set; }
}