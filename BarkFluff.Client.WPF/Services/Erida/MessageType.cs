using System;
using System.Collections.Generic;
using System.Text;

namespace BarkFluff.Client.WPF.Services.Erida
{
    public class MessageType
    {
        public MessageTypeEnum Type { get; set; }
        public enum MessageTypeEnum
        {
            Info,
            Warning,
            Error,
            Success
        }
    }
}
