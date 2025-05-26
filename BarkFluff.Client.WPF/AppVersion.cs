using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BarkFluff.Client.WPF
{
    public class AppVersion
    {
       public static string Version { get; set; } = "0.0.0.293";
        public static string VersionName { get; } = "α";
        public static string AppName 
        { 
            get {
#if DEBUG
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    Console.WriteLine("BarkFluff on Visual studio");
                }
#endif
                return "BarkFluff";
            } } 
    }
}
