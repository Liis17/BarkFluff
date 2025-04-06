using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BarkFluff.Client.WPF.MessagerData
{
    public class GlobalParam
    {
        public string Host { get; set; } = string.Empty;
        public string Port { get; set; } = string.Empty;
        public string Socket
        {
            get
            {
                if (Host == string.Empty || Port == string.Empty)
                {
                    return string.Empty;
                }
                return Host + ":" + Port;
            }
        }
    }
}
