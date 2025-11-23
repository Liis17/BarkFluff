using System;
using System.Collections.Generic;
using System.Text;

namespace BarkFluff.WebApi.Core.MessengerData.NonSavedData
{
    public class ForwardingLetter
    {
        public string Text { get; set; } = string.Empty;
        public List<string> FilesId { get; set; } = new List<string>();
    }
}
