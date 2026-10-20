using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace WhatThePort {
    public sealed class Scanner {
        sealed class Previous { public DateTime Start, At; public double Cpu; }
        readonly Dictionary<int, Previous> previous = new Dictionary<int, Previous>();
        readonly Dictionary<string, Server> tracked = new Dictionary<string, Server>();
        readonly Dictionary<string, Tuple<DateTime,string,string>> projects = new Dictionary<string, Tuple<DateTime,string,string>>();
        static readonly HashSet<string> Runtimes = new HashSet<string>(new [] { "node", "bun", "deno", "python", "python3", "pythonw", "uv", "uvicorn", "gunicorn", "ruby", "java", "javaw", "dotnet", "php", "go", "air", "cargo", "beam.smp", "elixir", "redis-server", "postgres", "mongod", "mysqld", "sqlservr", "wtp-tests" });
        public Snapshot Scan(Settings settings) {
            var output = new Snapshot(); DateTime now=output.At;
            var sockets=Native.Sockets(); var processes=Native.Processes();
            var listeners = new HashSet<int>(sockets.Where(s => s.State == 2).Select(s => s.Pid));
            var protectedIds=Policy.Ancestors(Process.GetCurrentProcess().Id, processes);
            var sampled=new Dictionary<int, ProcessNode>(); var live = new HashSet<string>();
            foreach (var group in sockets.Where(s => s.State == 2 && s.Port >= 1024).GroupBy(s => s.Pid)) {
                ProcessNode root;
                if (!processes.TryGetValue(group.Key, out root) || (!settings.AllListeners && !Runtimes.Contains(root.Name)) || !Native.IsOwned(root.Pid)) continue;
                var nodes = Policy.Tree(root.Pid, processes, listeners); var measured = new List<ProcessNode>();
                foreach (var node in nodes) {
                    ProcessNode value;
                    if (!sampled.TryGetValue(node.Pid, out value)) {
                        try {
                            DateTime start;double cpuTime;long memory;
                            if(!Native.SampleProcess(node.Pid,out start,out cpuTime,out memory))continue;
                            double cpu=0;
                            Previous old;
                            if (previous.TryGetValue(node.Pid,out old) && Native.SameStart(start,old.Start) && now > old.At) cpu=Math.Max(0, Math.Min(100, (cpuTime-old.Cpu)/(now-old.At).TotalMilliseconds/Environment.ProcessorCount*100));
                            previous[node.Pid]=new Previous { Start=start, At=now, Cpu=cpuTime };
                            value=new ProcessNode { Pid=node.Pid, Parent=node.Parent, Name=node.Name, Started=start, Memory=memory, Cpu=cpu, Depth=node.Depth };
                            sampled[node.Pid]=value;
                        } catch { continue; }
                    }
                    if(measured.Count>0&&value.Started<measured[0].Started)continue;
                    measured.Add(value);
                }
                var rootValue=measured.FirstOrDefault(p => p.Pid == root.Pid); if (rootValue == null) continue;
                var ports=group.Select(s => s.Port).Distinct().OrderBy(p => p).ToList();
                string key=rootValue.Identity;
                Server oldServer; tracked.TryGetValue(key,out oldServer);
                bool continuous=oldServer != null && (now-oldServer.SampledAt).TotalSeconds <= Math.Max(15,settings.ScanSeconds*3);
                var server=new Server { Key=key, Port=ports[0], Ports=ports, Address=group.OrderBy(s => s.Address.Contains(":") ? 1 : 0).First().Address, Name=root.Name,
                    Started=rootValue.Started, Observed=continuous ? oldServer.Observed : now, LastActive=continuous ? oldServer.LastActive : now, SampledAt=now,
                    Memory=measured.Sum(p => p.Memory), Cpu=measured.Sum(p => p.Cpu), Processes=measured,
                    Protected=Policy.IsProtectedName(root.Name) || protectedIds.Contains(root.Pid),
                    ProtectionReason=protectedIds.Contains(root.Pid) ? "This app or a parent process. Stopping it could close your working session." : Policy.IsProtectedName(root.Name) ? "Terminal, agent, system, database or container process. Stop it in its own app." : null,
                    Connections=sockets.Count(s => s.State == 5 && s.Pid == root.Pid && ports.Contains(s.Port)),
                    History=oldServer == null ? new List<Sample>() : new List<Sample>(oldServer.History) };
                if (server.Connections > 0 || server.Cpu >= 2) server.LastActive=now;
                ResolveProject(server,root.Pid,now);
                server.History.Add(new Sample { At=now, Memory=server.Memory, Cpu=server.Cpu }); Policy.PruneHistory(server.History,now);
                server.Warning=Policy.Warning(server,settings);
                tracked[key]=server; live.Add(key); output.Servers.Add(server);
            }
            foreach (var key in tracked.Keys.Where(k => !live.Contains(k)).ToList()) { tracked.Remove(key); projects.Remove(key); }
            foreach (var pid in previous.Keys.Where(p => !sampled.ContainsKey(p)).ToList()) previous.Remove(pid);
            output.Servers=output.Servers.OrderBy(s => s.Port).ToList();
            var clientSamples=new Dictionary<int,ProcessNode>(sampled);
            var connections=new Connections(sockets);
            foreach(var server in output.Servers){
                server.Agent=FindAgent(server.Processes[0],processes,clientSamples,now);
                foreach(var client in connections.ClientPids(server)){
                    ProcessNode process;if(!processes.TryGetValue(client.Key,out process))continue;
                    ProcessNode identity;
                    if(!clientSamples.TryGetValue(client.Key,out identity)){
                        DateTime start;double cpu;long memory;
                        if(!Native.SampleProcess(client.Key,out start,out cpu,out memory)||start>now)continue;
                        identity=new ProcessNode{Pid=process.Pid,Name=process.Name,Parent=process.Parent,Started=start};clientSamples[client.Key]=identity;
                    }
                    var project=output.Servers.FirstOrDefault(s=>s.Processes.Any(p=>p.Pid==identity.Pid&&Native.SameStart(p.Started,identity.Started)));
                    string agent=FindAgent(identity,processes,clientSamples,now);
                    server.Clients.Add(new ConnectedApp{Pid=identity.Pid,Started=identity.Started,Name=identity.Name,Project=project==null?null:project.Name,Agent=agent,Connections=client.Value});
                }
                server.Clients=server.Clients.OrderBy(c=>c.Project??c.Agent??c.Name).ThenBy(c=>c.Pid).ToList();
            }
            Native.Memory(out output.TotalMemory,out output.FreeMemory);
            return output;
        }
        static string FindAgent(ProcessNode child,IDictionary<int,ProcessNode> processes,IDictionary<int,ProcessNode> sampled,DateTime at){
            var seen=new HashSet<int>();DateTime childStart=child.Started;
            for(int pid=child.Pid;pid>0&&seen.Count<64&&seen.Add(pid);){
                ProcessNode node,identity;if(!processes.TryGetValue(pid,out node))break;
                if(!sampled.TryGetValue(pid,out identity)){
                    DateTime start;double cpu;long memory;
                    if(!Native.SampleProcess(pid,out start,out cpu,out memory)||start>at)break;
                    identity=new ProcessNode{Pid=pid,Started=start};sampled[pid]=identity;
                }
                if(identity.Started>childStart)break;childStart=identity.Started;
                if(node.Name=="codex"||node.Name=="claude")return node.Name=="codex"?"Codex":"Claude Code";
                pid=node.Parent;
            }
            return null;
        }
        void ResolveProject(Server s,int pid,DateTime now) {
            Tuple<DateTime,string,string> cached;
            if (!projects.TryGetValue(s.Key,out cached) || (now-cached.Item1).TotalSeconds > 30) {
                string folder=Native.CurrentDirectory(pid), branch=null;
                if (!String.IsNullOrEmpty(folder)) {
                    try {
                        var dir=new DirectoryInfo(folder);
                        for (int i=0;dir != null && i<12;i++,dir=dir.Parent) {
                            string git=Path.Combine(dir.FullName,".git");
                            if (File.Exists(git)) {
                                string marker=SmallText(git).Trim(); if (marker.StartsWith("gitdir: ",StringComparison.Ordinal)) git=Path.GetFullPath(Path.Combine(dir.FullName,marker.Substring(8)));
                            }
                            string head=Path.Combine(git,"HEAD");
                            if (File.Exists(head)) { string value=SmallText(head).Trim(); branch=value.StartsWith("ref: refs/heads/",StringComparison.Ordinal) ? value.Substring(16) : value.Substring(0,Math.Min(8,value.Length)); break; }
                        }
                    } catch { }
                }
                cached=Tuple.Create(now,folder,branch); projects[s.Key]=cached;
            }
            s.Folder=cached.Item2; s.Branch=cached.Item3;
            if (!String.IsNullOrEmpty(s.Folder)) { string name=Path.GetFileName(s.Folder.TrimEnd('\\','/')); if (!String.IsNullOrWhiteSpace(name)) s.Name=name; }
        }
        static string SmallText(string path) { using(var r=new StreamReader(path)) { char[] buffer=new char[4096]; int count=r.Read(buffer,0,buffer.Length); return new string(buffer,0,count); } }
    }
    public static class ProcessControl {
        public delegate bool StopNode(ProcessNode node,out string error);
        public static List<string> Stop(Server server) {
            return StopDetailed(server,Native.Stop).Errors;
        }
        public static StopResult StopDetailed(Server server,StopNode stop) {
            var result=new StopResult{Port=server.Port};
            if (server.Protected) {result.Errors.Add("This server is protected.");return result;}
            if ((DateTime.UtcNow-server.SampledAt).TotalSeconds > 30) {result.Errors.Add("Server data is stale. Refresh before stopping.");return result;}
            var nodes=server.Processes.OrderBy(p=>p.Depth).ToList();
            if(nodes.Count==0 || nodes[0].Depth!=0){result.Errors.Add("Server identity unavailable. Refresh before stopping.");return result;}
            if(Policy.IsProtectedName(nodes[0].Name)){result.Errors.Add("This server is protected.");return result;}
            // A root failure cancels descendant stops. A later failure cannot undo a successful root stop.
            foreach(var node in nodes){
                string error;
                if(Policy.IsProtectedName(node.Name)){result.Skipped++;continue;}
                if(stop(node,out error)){result.Stopped++;if(node.Depth==0)result.ListenerStopped=true;}
                else {result.Errors.Add(node.Name+" ("+node.Pid+"): "+error);if(node.Depth==0)break;}
            }
            return result;
        }
    }
    public sealed class StopResult {
        public int Port,Stopped,Skipped;
        public bool ListenerStopped;
        public List<string> Errors=new List<string>();
    }
}
