namespace BarkFluff.GrpcServer.Tracker;

public class RequestContext
{
    public string? OperationSystem { get; init; }

    public string? IpAddress { get; init; }

    public string? DeviceName { get; init; }

    public string? AppName { get; init; }

    public string? AppVersion { get; init; }

    public string? DeviceId { get; init; }
}
