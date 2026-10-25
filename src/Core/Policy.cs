using System;
using System.Collections.Generic;
using System.Linq;

namespace WhatThePort {
    public static class Policy {
        static readonly HashSet<string> ProtectedNames = new HashSet<string>(new [] { "postgres", "postgresql", "redis", "redis-server", "mongod", "mongos", "mysql", "mysqld", "mariadbd", "sqlservr", "memcached", "cockroach", "influxd", "system", "registry", "svchost", "services", "lsass", "csrss", "wininit", "winlogon", "explorer", "dwm", "cmd", "powershell", "pwsh", "windowsterminal", "openconsole", "conhost", "bash", "wsl", "wslhost", "code", "codex", "claude", "whattheport", "wtp" }, StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> InfrastructureNames = new HashSet<string>(new [] { "wmux", "cagent", "boiler-ops-agent", "docker", "dockerd", "docker desktop", "com.docker.backend", "com.docker.proxy", "containerd" }, StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> CriticalNames = new HashSet<string>(new [] { "system", "registry", "smss", "svchost", "services", "lsass", "csrss", "wininit", "winlogon", "explorer", "dwm", "fontdrvhost", "sihost", "whattheport", "wtp" }, StringComparer.OrdinalIgnoreCase);
        public static bool IsCriticalName(string name) { return CriticalNames.Contains(System.IO.Path.GetFileNameWithoutExtension(name ?? "")); }
        public static bool IsProtectedName(string name) { string file=System.IO.Path.GetFileNameWithoutExtension(name ?? ""); return ProtectedNames.Contains(file) || InfrastructureNames.Contains(file) || CriticalNames.Contains(file); }
        public static HashSet<int> Ancestors(int pid, IDictionary<int, ProcessNode> all) {
            var result = new HashSet<int>(); ProcessNode node;
            while (pid > 0 && result.Add(pid) && all.TryGetValue(pid, out node)) pid = node.Parent;
            return result;
        }
        public static List<ProcessNode> Tree(int root, IDictionary<int, ProcessNode> all, HashSet<int> listeners) {
            var result = new List<ProcessNode>(); var seen = new HashSet<int>();
            Visit(root, 0, all, listeners, seen, result);
            return result;
        }
        static void Visit(int pid, int depth, IDictionary<int, ProcessNode> all, HashSet<int> listeners, HashSet<int> seen, List<ProcessNode> result) {
            ProcessNode node;
            if (!seen.Add(pid) || !all.TryGetValue(pid, out node) || depth > 64) return;
            if (depth > 0 && (listeners.Contains(pid) || IsProtectedName(node.Name))) return;
            result.Add(new ProcessNode { Pid=node.Pid, Parent=node.Parent, Name=node.Name, Started=node.Started, Depth=depth });
            foreach (var child in all.Values.Where(p => p.Parent == pid)) Visit(child.Pid, depth+1, all, listeners, seen, result);
        }
        public static string Warning(Server s, Settings settings) {
            if (s.Memory >= settings.MemoryGB * Format.GB) return "High memory · " + Format.Bytes(s.Memory);
            if (settings.GrowthMB > 0 && s.History.Count > 1 && (s.SampledAt - s.History[0].At).TotalSeconds >= 60) {
                long growth = s.Memory - s.History.Min(h => h.Memory);
                if (growth >= settings.GrowthMB * Format.MB) return "+" + Format.Bytes(growth) + " in 10 min";
            }
            if (s.History.Count >= 3 && s.History.Skip(s.History.Count-3).All(h => h.Cpu >= settings.CpuPercent)) return "High CPU · " + Format.Percent(s.Cpu);
            return null;
        }
        public static bool CleanupCandidate(Server s, Settings settings, DateTime now) {
            return !s.Protected && String.IsNullOrEmpty(s.Warning) && s.Connections == 0 && s.Cpu < 2 &&
                (now-s.SampledAt).TotalSeconds <= Math.Max(15, settings.ScanSeconds*3) &&
                now-s.LastActive >= TimeSpan.FromHours(settings.IdleHours) && now-s.Observed >= TimeSpan.FromHours(settings.IdleHours);
        }
        public static void PruneHistory(List<Sample> history, DateTime now) {
            history.RemoveAll(h => now-h.At > TimeSpan.FromMinutes(10));
            if (history.Count > 601) history.RemoveRange(0, history.Count-601);
        }
    }
}
