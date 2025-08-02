using LiteDB;

using System;
using System.Collections.Generic;
using System.Text;

namespace BarkFluff.Client.WPF.Services.App.Caching
{
    public class CachedFile
    {
        [BsonId]
        public string FileId { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public string LocalPath { get; set; } = string.Empty;
    }
}
