using System;
using System.Collections.Generic;
using System.Text;

namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    public class ServerDataElement
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public string UserCount {  get; set; } = string.Empty;

    }
}
