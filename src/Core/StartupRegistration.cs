using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WhatThePort {
    public static class StartupRegistration {
        const string Name="What the Port.lnk";
        public static bool IsEnabled(string folder){return File.Exists(Path.Combine(folder,Name));}
        public static void Set(bool enabled,string executable,string folder){
            string target=Path.Combine(folder,Name);
            if(!enabled){if(File.Exists(target))File.Delete(target);return;}
            if(!Path.IsPathRooted(executable)||!File.Exists(executable))throw new ArgumentException("Choose an existing executable for startup.");
            executable=Path.GetFullPath(executable);
            Directory.CreateDirectory(folder);
            string temporary=Path.Combine(folder,"WhatThePort-"+Guid.NewGuid().ToString("N")+".lnk");
            object shell=null,shortcut=null;
            try {
                shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true));
                if(File.Exists(target)){
                    shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{target});
                    var existing=shortcut.GetType();
                    if(String.Equals((string)existing.InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null),executable,StringComparison.OrdinalIgnoreCase)
                        &&(string)existing.InvokeMember("Arguments",BindingFlags.GetProperty,null,shortcut,null)=="--background"
                        &&String.Equals((string)existing.InvokeMember("WorkingDirectory",BindingFlags.GetProperty,null,shortcut,null),Path.GetDirectoryName(executable),StringComparison.OrdinalIgnoreCase))return;
                    Marshal.FinalReleaseComObject(shortcut);shortcut=null;
                }
                shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{temporary});
                var type=shortcut.GetType();
                type.InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{executable});
                type.InvokeMember("Arguments",BindingFlags.SetProperty,null,shortcut,new object[]{"--background"});
                type.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{Path.GetDirectoryName(executable)});
                type.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
                if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);
            } finally {
                if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);
                if(shell!=null)Marshal.FinalReleaseComObject(shell);
                if(File.Exists(temporary))File.Delete(temporary);
            }
        }
    }
}
