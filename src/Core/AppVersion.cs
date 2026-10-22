using System.Reflection;
#if !SINGLE_FILE
[assembly: AssemblyVersion(WhatThePort.AppVersion.Current)]
[assembly: AssemblyFileVersion(WhatThePort.AppVersion.Current)]
#endif
namespace WhatThePort {
    public static class AppVersion { public const string Current="1.1.2"; }
}
