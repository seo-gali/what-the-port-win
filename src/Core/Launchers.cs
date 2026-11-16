using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WhatThePort {
    public static class Launchers {
        public static string QuotePowerShell(string text) { return "'" + (text ?? "").Replace("'", "''") + "'"; }
        public static string SessionScript(ProjectLink link) {
            link.Validate();
            if (String.IsNullOrEmpty(link.Session)) throw new ArgumentException("Register the exact session ID first.");
            return "Set-Location -LiteralPath " + QuotePowerShell(link.Folder) + "; & " + (link.Agent == "Codex" ? "codex resume " : "claude --resume ") + QuotePowerShell(link.Session);
        }
        public static ProcessStartInfo TerminalStartInfo(string folder,string terminal,ProjectLink session) {
            if(String.IsNullOrEmpty(folder)||!Directory.Exists(folder))throw new IOException("The project folder is unavailable.");
            string script=session==null?"Set-Location -LiteralPath "+QuotePowerShell(folder):SessionScript(session);
            string args="-NoLogo -NoExit -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
            if(terminal=="Windows Terminal")return new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"Microsoft\WindowsApps\wt.exe"),"new-tab \""+powershell+"\" "+args){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=folder};
            if(terminal!="PowerShell")throw new ArgumentException("Invalid terminal.");
            return new ProcessStartInfo(powershell,args){UseShellExecute=true,WorkingDirectory=folder};
        }
        public static void Terminal(string folder,string terminal,ProjectLink session) {
            var start=TerminalStartInfo(folder,terminal,session);
            try {Process.Start(start);}
            catch(System.ComponentModel.Win32Exception){if(terminal=="Windows Terminal")throw new IOException("Windows Terminal was not found. Choose PowerShell in Settings.");throw;}
        }
        public static void OpenLocal(Server server) { Process.Start(new ProcessStartInfo(server.Url) { UseShellExecute=true }); }
        public static void OpenPreview(ProjectLink link) {
            if (link == null || !ProjectLink.ValidPreview(link.Preview)) throw new ArgumentException("Register a Vercel preview URL first.");
            Process.Start(new ProcessStartInfo(link.Preview) { UseShellExecute=true });
        }
    }
}
