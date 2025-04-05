namespace BarkFluff.Server.CLI
{
    public class MySettings
    {
        public string Address { get; set; }
        public int Port { get; set; }
        public CertificateSettings Certificate { get; set; }

        public class CertificateSettings
        {
            public string Path { get; set; }
            public string Password { get; set; }
        }
    }
}
