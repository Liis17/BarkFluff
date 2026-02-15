namespace BarkFluff.Navigator.Domain;

using System.ComponentModel.DataAnnotations;

public class ServerInfo
{
    [Key]
    public long Id { get; set; }
    public required string BeaconHost { get; set; }
    public int BeaconPort { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public string ServerPublicName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string ColorLiteHex { get; set; } = string.Empty;
    public string ColorMainHex { get; set; } = string.Empty;
    public string ColorHardHex { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public required string AddedBy { get; set; }
}