using System.Collections.ObjectModel;

namespace BarkFluff.DBEditor.Models
{
    public class ServiceGroup
    {
        public int ServiceId { get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public ObservableCollection<ConfigItem> Items { get; set; } = [];

        public static string GetServiceName(int serviceId) => serviceId switch
        {
            0 => "Navigator",
            1 => "Identity Service",
            2 => "Users Service",
            3 => "Server Properties",
            4 => "Email Service",
            5 => "Files Service",
            6 => "Messages Service",
            7 => "Updates Service",
            8 => "Notifications Service",
            9 => "Onliner Service",
            10 => "FastAuth Service",
            11 => "Beacon Service",
            12 => "Configuration Service",
            _ => $"Service {serviceId}"
        };
    }
}