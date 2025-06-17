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
    
    public DateTime CreatedAt { get; set; }
    
    public required string AddedBy { get; set; }
}