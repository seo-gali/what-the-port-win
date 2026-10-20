using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace WhatThePort {
    [DataContract] public sealed class Sample {
        [DataMember] public DateTime At;
        [DataMember] public long Memory;
        [DataMember] public double Cpu;
    }
    [DataContract] public sealed class ProcessNode {
        [DataMember] public int Pid;
        [DataMember] public int Parent;
        [DataMember] public string Name;
        [DataMember] public DateTime Started;
        [DataMember] public long Memory;
        [DataMember] public double Cpu;
        [DataMember] public int Depth;
        public string Identity { get { return Pid + ":" + Started.Ticks; } }
    }
    public sealed class SocketRow {
        public int Pid, Port, State, RemotePort;
        public string Address, RemoteAddress;
    }
    [DataContract] public sealed class ConnectedApp {
        [DataMember] public int Pid;
        [DataMember] public DateTime Started;
        [DataMember] public string Name,Project,Agent;
        [DataMember] public int Connections;
    }
    [DataContract] public sealed class Server {
        [DataMember] public string Key;
        [DataMember] public int Port;
        [DataMember] public List<int> Ports = new List<int>();
        [DataMember] public string Address;
        [DataMember] public string Name;
        [DataMember] public string Folder;
        [DataMember] public string Branch;
        [DataMember] public string Agent;
        [DataMember] public DateTime Started;
        [DataMember] public DateTime Observed;
        [DataMember] public DateTime LastActive;
        [DataMember] public DateTime SampledAt;
        [DataMember] public long Memory;
        [DataMember] public double Cpu;
        [DataMember] public int Connections;
        [DataMember] public List<ConnectedApp> Clients = new List<ConnectedApp>();
        [DataMember] public bool Protected;
        [DataMember] public string ProtectionReason;
        [DataMember] public string Warning;
        [DataMember] public List<ProcessNode> Processes = new List<ProcessNode>();
        [DataMember] public List<Sample> History = new List<Sample>();
        public string Url { get { string host=Address; if(String.IsNullOrEmpty(host)||host=="0.0.0.0")host="127.0.0.1";else if(host=="::")host="::1";if(host.Contains(":"))host="["+host+"]";return "http://"+host+":"+Port; } }
    }
    [DataContract] public sealed class Snapshot {
        [DataMember] public DateTime At = DateTime.UtcNow;
        [DataMember] public long TotalMemory;
        [DataMember] public long FreeMemory;
        [DataMember] public List<Server> Servers = new List<Server>();
        [DataMember] public string Error;
        public long ServerMemory { get { return Servers.Sum(s => s.Memory); } }
        public double Cpu { get { return Servers.Sum(s => s.Cpu); } }
    }
    public static class Format {
        public const long MB = 1048576;
        public const long GB = 1073741824;
        public static string Bytes(long bytes) { return bytes >= GB ? (bytes / (double)GB).ToString("0.00", CultureInfo.InvariantCulture) + " GB" : (bytes / (double)MB).ToString("0", CultureInfo.InvariantCulture) + " MB"; }
        public static string Total(long bytes) { return bytes >= GB ? (bytes / (double)GB).ToString("0.0", CultureInfo.InvariantCulture) + " GB" : Bytes(bytes); }
        public static string Duration(TimeSpan span) { return span.TotalDays >= 1 ? ((int)span.TotalDays) + "d" : span.TotalHours >= 1 ? ((int)span.TotalHours) + "h" : span.TotalMinutes >= 1 ? ((int)span.TotalMinutes) + "m" : "<1m"; }
        public static string Percent(double value) { return value > 0 && value < .1 ? "<0.1%" : value.ToString(value < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + "%"; }
        public static string[] Colors = { "#6EC7ED", "#B599F0", "#EB9CD4", "#7D9CF2", "#7DDBE0", "#D9A3F2", "#ABC2E0" };
        public static string PortColor(int port) { return Colors[Math.Abs(port) % Colors.Length]; }
    }
}
