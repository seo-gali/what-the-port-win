using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace WhatThePort {
    [DataContract] public sealed class Settings {
        [DataMember] public int ScanSeconds = 3;
        [DataMember] public double MemoryGB = 2;
        [DataMember] public int GrowthMB = 500;
        [DataMember] public int CpuPercent = 80;
        [DataMember] public double IdleHours = 4;
        [DataMember] public string Cleanup = "Off";
        [DataMember] public string Terminal = "PowerShell";
        [DataMember] public bool Notifications = true;
        [DataMember] public bool AllListeners = false;
        [DataMember] public string Language = "ko";
        [DataMember] public int? PanelLeft;
        [DataMember] public int? PanelBottom;
        [DataMember] public int? PanelSize;
        [DataMember] public int? PanelOpacity;
        public void Validate() {
            if (String.IsNullOrEmpty(Language)) Language = "ko";
            if (Language != "ko" && Language != "en") throw new ArgumentException("Invalid language.");
            if (ScanSeconds < 1 || ScanSeconds > 30 || Double.IsNaN(MemoryGB) || MemoryGB < .1 || MemoryGB > 1024 || GrowthMB < 0 || GrowthMB > 1048576 || CpuPercent < 1 || CpuPercent > 100 || Double.IsNaN(IdleHours) || IdleHours < .05 || IdleHours > 720) throw new ArgumentException("Check the sampling, alert and idle thresholds.");
            if (!new [] {"Off", "Ask", "Automatic"}.Contains(Cleanup)) throw new ArgumentException("Invalid cleanup mode.");
            if (!new [] {"PowerShell", "Windows Terminal"}.Contains(Terminal)) throw new ArgumentException("Invalid terminal.");
            if (PanelLeft.HasValue != PanelBottom.HasValue || (PanelLeft.HasValue && (Math.Abs((long)PanelLeft.Value)>1000000 || Math.Abs((long)PanelBottom.Value)>1000000))) throw new ArgumentException("Invalid saved panel position.");
            if (PanelSize.HasValue && (PanelSize<0 || PanelSize>3)) throw new ArgumentException("Invalid panel size.");
            if (PanelOpacity.HasValue && (PanelOpacity<40 || PanelOpacity>100)) throw new ArgumentException("Invalid background opacity.");
        }
        public Settings Copy() { return (Settings)MemberwiseClone(); }
    }
    [DataContract] public sealed class ProjectLink {
        [DataMember] public string Folder;
        [DataMember] public int Port;
        [DataMember] public string Agent;
        [DataMember] public string Session;
        [DataMember] public string Preview;
        public void Validate() {
            if (Port < 1 || Port > 65535) throw new ArgumentException("Port must be between 1 and 65535.");
            if (String.IsNullOrWhiteSpace(Folder) || !Path.IsPathRooted(Folder) || !Directory.Exists(Folder)) throw new ArgumentException("Choose an existing absolute project folder.");
            if (!String.IsNullOrEmpty(Agent) && Agent != "Codex" && Agent != "Claude Code") throw new ArgumentException("Choose Codex or Claude Code.");
            if (!String.IsNullOrEmpty(Session) && (String.IsNullOrEmpty(Agent) || !Regex.IsMatch(Session, @"\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,127}\z"))) throw new ArgumentException("Session ID must contain only letters, digits, dashes and underscores.");
            if (!String.IsNullOrEmpty(Preview) && !ValidPreview(Preview)) throw new ArgumentException("Use an HTTPS preview URL on vercel.app (no credentials or custom ports).");
        }
        public static bool ValidPreview(string value) {
            Uri u;
            return Uri.TryCreate(value, UriKind.Absolute, out u) && u.Scheme == "https" && String.IsNullOrEmpty(u.UserInfo) && u.IsDefaultPort && u.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase) && u.Host.Length > ".vercel.app".Length;
        }
    }
    public sealed class Store {
        public readonly string Root;
        public string LastError;
        public Store(string root) { Root=root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhatThePort"); }
        public Settings LoadSettings() {
            try { var s=Read<Settings>("settings.json") ?? new Settings(); s.Validate(); return s; }
            catch { LastError="Settings could not be read. Safe defaults are active; save Settings to repair."; return new Settings(); }
        }
        public List<ProjectLink> LoadLinks() {
            try { return (Read<List<ProjectLink>>("links.json") ?? new List<ProjectLink>()).Where(l => l != null).ToList(); }
            catch { LastError="Project links could not be read. Register the links again."; return new List<ProjectLink>(); }
        }
        public void SaveSettings(Settings s) { s.Validate(); Write("settings.json", s); }
        public void SaveLink(ProjectLink link) {
            link.Validate(); var links = LoadLinks();
            links.RemoveAll(l => l.Port == link.Port && String.Equals(l.Folder, link.Folder, StringComparison.OrdinalIgnoreCase));
            links.Add(link); Write("links.json", links);
        }
        public ProjectLink LinkFor(Server server) {
            // Port alone is not a durable project identity. Unknown folders never inherit old links.
            return LoadLinks().FirstOrDefault(l => l.Port == server.Port && !String.IsNullOrEmpty(server.Folder) && String.Equals(l.Folder.TrimEnd('\\'), server.Folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        }
        T Read<T>(string name) where T:class {
            string path=Path.Combine(Root,name); if (!File.Exists(path)) return null;
            using (var file=File.OpenRead(path)) { if (file.Length > 1024*1024) throw new InvalidDataException(); return new DataContractJsonSerializer(typeof(T)).ReadObject(file) as T; }
        }
        void Write<T>(string name, T data) {
            Directory.CreateDirectory(Root); string target=Path.Combine(Root,name), temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using (var file=File.Create(temp)) new DataContractJsonSerializer(typeof(T)).WriteObject(file,data);
                if (File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static string Json<T>(T data) { using (var stream=new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(stream,data); return Encoding.UTF8.GetString(stream.ToArray()); } }
    }
}
