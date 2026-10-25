using System;
using System.IO;
using Microsoft.Win32;

namespace WhatThePort {
    public static class StartupRegistration {
        public const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name="What the Port";
        public static bool IsEnabled(string folder,string registryPath=RunKey){
            if(File.Exists(Path.Combine(folder,Name+".lnk")))return true;
            using(var key=Registry.CurrentUser.OpenSubKey(registryPath))return key!=null&&!String.IsNullOrWhiteSpace(key.GetValue(Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string);
        }
        public static void Set(bool enabled,string executable,string folder,string registryPath=RunKey){
            string legacy=Path.Combine(folder,Name+".lnk"),command=null;
            if(enabled){
                if(!Path.IsPathRooted(executable)||!File.Exists(executable))throw new ArgumentException("Choose an existing executable for startup.");
                command="\""+Path.GetFullPath(executable)+"\" --background";
                if(command.Length>260)throw new ArgumentException("The Windows startup command is longer than 260 characters.");
                if(Directory.Exists(legacy))throw new IOException("The old startup entry is a directory and cannot be replaced.");
            }
            using(var key=enabled?Registry.CurrentUser.CreateSubKey(registryPath):Registry.CurrentUser.OpenSubKey(registryPath,true)){
                if(key==null){if(File.Exists(legacy))File.Delete(legacy);return;}
                object previous=key.GetValue(Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                var kind=previous==null?RegistryValueKind.String:key.GetValueKind(Name);
                string existing=previous as string;
                bool same=kind==RegistryValueKind.String&&String.Equals(existing,command,StringComparison.OrdinalIgnoreCase)&&existing!=null&&existing.EndsWith(" --background",StringComparison.Ordinal);
                bool changed=enabled?!same:previous!=null;
                if(changed){if(enabled)key.SetValue(Name,command,RegistryValueKind.String);else key.DeleteValue(Name,false);}
                try {if(File.Exists(legacy))File.Delete(legacy);}
                catch(Exception failure){
                    if(changed)try{if(previous==null)key.DeleteValue(Name,false);else key.SetValue(Name,previous,kind);}
                    catch(Exception rollback){throw new IOException("Startup cleanup and registry rollback failed.",new AggregateException(failure,rollback));}
                    throw;
                }
            }
        }
    }
}
