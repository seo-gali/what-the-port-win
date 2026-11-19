using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace WhatThePort {
    public enum SecurityEvent { ActionFailed,PreferencesSaved,ServerStopCompleted,ServerStopFailed,ServerStopRejected,ServerStopSkipped }
    public static class SecurityLog {
        // shortcut: one GUI writer; coordinate writes if CLI logging is added.
        public static void Write(string root,SecurityEvent item){
            try{
                Directory.CreateDirectory(root);string file=Path.Combine(root,"security.log");
                if(File.Exists(file)&&new FileInfo(file).Length>=128*1024){
                    string previous=file+".1";if(File.Exists(previous))File.Delete(previous);File.Move(file,previous);
                }
                File.AppendAllText(file,DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture)+"\t"+item+Environment.NewLine,new UTF8Encoding(false));
            }catch{
                // Logging failure must not interrupt the action or expose its inputs.
            }
        }
    }
}