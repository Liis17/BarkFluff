using System;
using System.Collections.Generic;
using System.Text;

namespace BarkFluff.Client.WPF.Services.App.Caching
{
    public class CachedMessage
    {
        public long Id { get; set; }
        public string ChatId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public long? RepliedToId { get; set; }

        public List<string> FileIds { get; set; } = new();
    }
}
