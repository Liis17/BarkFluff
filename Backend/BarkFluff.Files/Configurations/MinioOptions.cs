namespace BarkFluff.Files.Configurations;

public class MinioOptions
{
    public string ServiceUrl { get; set; } 
    
    public string AccessKey { get; set; }
    
    public string SecretKey { get; set; }

    public bool ForcePathStyle => true;
}