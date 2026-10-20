using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WhatThePort;

public static class CoreTests {
    static int passed,failed;
    static string temp;
    public static int Main(string[] args) {
        if(args.Length>0&&args[0]=="--fixture")return Fixture();
        temp=Path.Combine(Path.GetTempPath(),"wtp-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try{Units();ConnectionTests();StartupTests();Integration();}catch(Exception e){failed++;Console.WriteLine("FAIL unexpected: "+e);}
        finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
        Console.WriteLine("\n"+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
    static void Check(string name,bool condition){if(condition){passed++;Console.WriteLine("PASS "+name);}else{failed++;Console.WriteLine("FAIL "+name);}}
    static void StartupTests(){
        string folder=Path.Combine(temp,"startup"),executable=Path.Combine(temp,"portable app.exe");File.WriteAllText(executable,"fixture");
        StartupRegistration.Set(false,executable,folder);Check("startup defaults to off without creating directories",!StartupRegistration.IsEnabled(folder)&&!Directory.Exists(folder));
        StartupRegistration.Set(true,executable,folder);Check("startup creates only the app shortcut",StartupRegistration.IsEnabled(folder)&&Directory.GetFiles(folder).Length==1);
        object shell=null,shortcut=null;
        try{
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true));shortcut=shell.GetType().InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{Path.Combine(folder,"What the Port.lnk")});
            var type=shortcut.GetType();
            Check("startup keeps path with spaces and launches in background",(string)type.InvokeMember("TargetPath",System.Reflection.BindingFlags.GetProperty,null,shortcut,null)==executable&&(string)type.InvokeMember("Arguments",System.Reflection.BindingFlags.GetProperty,null,shortcut,null)=="--background");
        }finally{if(shortcut!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);if(shell!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
        StartupRegistration.Set(true,executable,folder);Check("saving startup again replaces rather than duplicates shortcut",Directory.GetFiles(folder).Length==1);
        File.WriteAllText(Path.Combine(folder,"unrelated.txt"),"keep");StartupRegistration.Set(false,executable,folder);Check("startup off removes only app registration",!StartupRegistration.IsEnabled(folder)&&File.Exists(Path.Combine(folder,"unrelated.txt")));
        Throws("startup rejects nonexistent executable",()=>StartupRegistration.Set(true,Path.Combine(temp,"missing.exe"),folder));
        Throws("invalid opacity rejected",()=>new Settings{PanelOpacity=0}.Validate());Throws("invalid panel size rejected",()=>new Settings{PanelSize=4}.Validate());
    }
    static void ConnectionTests(){
        var server=new Server{Ports=new List<int>{8000},Processes=new List<ProcessNode>{new ProcessNode{Pid=10}}};
        var rows=new List<SocketRow>{
            new SocketRow{Pid=10,State=5,Address="127.0.0.1",Port=8000,RemoteAddress="127.0.0.1",RemotePort=52000},
            new SocketRow{Pid=20,State=5,Address="127.0.0.1",Port=52000,RemoteAddress="127.0.0.1",RemotePort=8000},
            new SocketRow{Pid=21,State=5,Address="127.0.0.2",Port=52000,RemoteAddress="127.0.0.1",RemotePort=8000},
            new SocketRow{Pid=22,State=2,Address="127.0.0.1",Port=52000,RemoteAddress="127.0.0.1",RemotePort=8000}};
        var clients=new Connections(rows).ClientPids(server);Check("client attribution matches both reversed endpoints and established state",clients.Count==1&&clients[20]==1);
        rows[1].Address="::ffff:127.0.0.1";Check("mapped IPv6 endpoint matches IPv4",new Connections(rows).ClientPids(server).ContainsKey(20));
        rows[0].Address=rows[0].RemoteAddress=rows[1].Address=rows[1].RemoteAddress="::1";Check("IPv6 peers correlate",new Connections(rows).ClientPids(server).ContainsKey(20));
        rows[0].Address="fe80::1%3";rows[0].RemoteAddress="fe80::2%3";rows[1].Address="fe80::2%4";rows[1].RemoteAddress="fe80::1%3";Check("different IPv6 scopes cannot identify a caller",new Connections(rows).ClientPids(server).Count==0);
        Check("unknown process tree gives no attribution",new Connections(rows).ClientPids(new Server()).Count==0);
    }
    static void Throws(string name,Action action){try{action();Check(name,false);}catch(ArgumentException){Check(name,true);}}
    static Server Idle(DateTime now){return new Server{Observed=now.AddHours(-5),LastActive=now.AddHours(-5),SampledAt=now,Memory=100*Format.MB};}
    static void Units(){
        var settings=new Settings();DateTime now=DateTime.UtcNow;
        StopOutcomes(now);
        Check("Korean is the default language",settings.Language=="ko");
        var oldSettings=new Settings{Language=null};oldSettings.Validate();Check("legacy settings migrate to Korean",oldSettings.Language=="ko");
        Throws("unsupported language rejected",()=>new Settings{Language="xx"}.Validate());
        Check("automatic cleanup defaults to Off",settings.Cleanup=="Off");
        foreach(var name in new[]{"postgres","redis-server.exe","mongod","mysqld","sqlservr","powershell","codex","claude","wtp"})Check("protected: "+name,Policy.IsProtectedName(name));
        Check("development runtime can be stopped",!Policy.IsProtectedName("node"));
        foreach(var name in new[]{"wmux.exe","cagent.exe","docker.exe","dockerd","com.docker.backend.exe","boiler-ops-agent"})Check("infrastructure is protected: "+name,Policy.IsProtectedName(name));
        var all=new Dictionary<int,ProcessNode>{{1,new ProcessNode{Pid=1,Name="powershell"}},{2,new ProcessNode{Pid=2,Parent=1,Name="node"}},{3,new ProcessNode{Pid=3,Parent=2,Name="worker"}},{4,new ProcessNode{Pid=4,Parent=2,Name="python"}},{5,new ProcessNode{Pid=5,Parent=4,Name="worker"}},{6,new ProcessNode{Pid=6,Parent=2,Name="postgres"}}};
        var tree=Policy.Tree(2,all,new HashSet<int>{2,4});
        Check("tree excludes shell, separate listener and protected database",tree.Select(p=>p.Pid).SequenceEqual(new[]{2,3}));
        Check("ancestor cycle terminates",Policy.Ancestors(2,all).SetEquals(new[]{1,2}));
        all[1].Parent=2;Check("cyclic snapshots are bounded",Policy.Ancestors(2,all).Count==2);
        Check("matching PID start accepted",Native.SameStart(now,now));Check("PID reuse rejected",!Native.SameStart(now,now.AddMilliseconds(1)));
        var idle=Idle(now);Check("observed idle server eligible",Policy.CleanupCandidate(idle,settings,now));
        idle.Protected=true;Check("protected idle excluded",!Policy.CleanupCandidate(idle,settings,now));idle.Protected=false;
        idle.Warning="High memory";Check("warning server excluded",!Policy.CleanupCandidate(idle,settings,now));idle.Warning=null;
        idle.Connections=1;Check("connected server excluded",!Policy.CleanupCandidate(idle,settings,now));idle.Connections=0;
        idle.Cpu=2;Check("active CPU excluded",!Policy.CleanupCandidate(idle,settings,now));idle.Cpu=0;
        idle.Observed=now;Check("old process newly observed is not idle",!Policy.CleanupCandidate(idle,settings,now));idle.Observed=now.AddHours(-5);
        idle.SampledAt=now.AddMinutes(-1);Check("stale sample excludes automatic stop",!Policy.CleanupCandidate(idle,settings,now));
        var high=new Server{Memory=3*Format.GB,SampledAt=now};Check("absolute memory warning",Policy.Warning(high,settings).StartsWith("High memory"));
        high.Memory=900*Format.MB;high.History.Add(new Sample{At=now.AddMinutes(-3),Memory=200*Format.MB});high.History.Add(new Sample{At=now,Memory=high.Memory});Check("growth warning",Policy.Warning(high,settings).StartsWith("+700 MB"));
        high.History[0].At=now.AddSeconds(-20);Check("no growth warning from brief startup",Policy.Warning(high,settings)==null);
        high.History=new List<Sample>{new Sample{At=now.AddSeconds(-6),Memory=high.Memory,Cpu=99},new Sample{At=now.AddSeconds(-3),Memory=high.Memory,Cpu=99},new Sample{At=now,Memory=high.Memory,Cpu=99}};high.Cpu=99;Check("sustained CPU warning",Policy.Warning(high,settings).StartsWith("High CPU"));
        var history=Enumerable.Range(0,800).Select(i=>new Sample{At=now.AddSeconds(-i)}).ToList();Policy.PruneHistory(history,now);Check("bounded 10 minute history",history.Count==601&&history.All(h=>(now-h.At).TotalMinutes<=10));
        Check("stable port identity color",Format.PortColor(3000)==Format.PortColor(3000));
        Check("valid Vercel preview",ProjectLink.ValidPreview("https://my-app.vercel.app/path?q=1"));
        foreach(var url in new[]{"http://my-app.vercel.app","https://vercel.app.evil.com","https://x.vercel.app.evil.com","file:///C:/Windows","javascript:alert(1)","https://user@x.vercel.app","https://x.vercel.app:8443","https://vercel.app"})Check("unsafe preview rejected: "+url,!ProjectLink.ValidPreview(url));
        var link=new ProjectLink{Folder=temp,Port=3000,Agent="Codex",Session="abc-123"};link.Validate();
        Check("Codex resume is exact session",Launchers.SessionScript(link).EndsWith("& codex resume 'abc-123'"));
        link.Agent="Claude Code";Check("Claude resume is exact session",Launchers.SessionScript(link).EndsWith("& claude --resume 'abc-123'"));
        link.Session="x'; exit; '";Throws("session shell injection rejected",()=>link.Validate());link.Session="--help";Throws("session flag injection rejected",()=>link.Validate());link.Session="abc-123";
        Check("PowerShell apostrophe safely quoted",Launchers.QuotePowerShell("C:\\dev\\it's $(test)")=="'C:\\dev\\it''s $(test)'");
        var store=new Store(temp);store.SaveSettings(settings);Check("settings round-trip",store.LoadSettings().IdleHours==4&&store.LoadSettings().Notifications);
        settings.Language="en";store.SaveSettings(settings);Check("English preference survives restart",new Store(temp).LoadSettings().Language=="en");
        settings.Language="ko";store.SaveSettings(settings);Check("Korean preference survives restart",new Store(temp).LoadSettings().Language=="ko");
        settings.PanelLeft=-1500;settings.PanelBottom=920;store.SaveSettings(settings);var positioned=store.LoadSettings();Check("panel position persists across restart with negative monitor origin",positioned.PanelLeft==-1500&&positioned.PanelBottom==920);
        settings.PanelLeft=null;settings.PanelBottom=null;store.SaveSettings(settings);Check("panel position reset persists",!store.LoadSettings().PanelLeft.HasValue&&!store.LoadSettings().PanelBottom.HasValue);
        settings.Cleanup="Ask";store.SaveSettings(settings);Check("atomic settings replacement",store.LoadSettings().Cleanup=="Ask");
        File.WriteAllText(Path.Combine(temp,"settings.json"),"{bad");Check("corrupt settings use safe defaults",store.LoadSettings().Cleanup=="Off"&&store.LastError!=null);
        settings.IdleHours=Double.NaN;Throws("NaN threshold rejected",()=>settings.Validate());settings.IdleHours=4;settings.ScanSeconds=0;Throws("invalid sampling interval rejected",()=>settings.Validate());
        store.SaveLink(link);Check("project link round-trip",store.LinkFor(new Server{Port=3000,Folder=temp}).Session=="abc-123");
        Check("port reuse cannot inherit another project session",store.LinkFor(new Server{Port=3000,Folder=temp+"-different"})==null);
        Check("unknown folder cannot inherit old session",store.LinkFor(new Server{Port=3000})==null);
    }
    static void StopOutcomes(DateTime now){
        var server=new Server{Port=3000,SampledAt=now,Processes=new List<ProcessNode>{new ProcessNode{Pid=10,Name="node",Depth=0},new ProcessNode{Pid=11,Name="worker",Depth=1}}};
        var called=new List<int>();
        ProcessControl.StopNode partial=delegate(ProcessNode node,out string error){called.Add(node.Pid);error=node.Pid==11?"Access denied":null;return error==null;};
        var result=ProcessControl.StopDetailed(server,partial);
        Check("partial stop retains root success and child error",result.ListenerStopped&&result.Stopped==1&&result.Errors.Count==1&&result.Errors[0].Contains("worker (11)"));
        called.Clear();result=ProcessControl.StopDetailed(server,delegate(ProcessNode node,out string error){called.Add(node.Pid);error="Denied";return false;});
        Check("root failure never attempts descendants",!result.ListenerStopped&&result.Stopped==0&&called.SequenceEqual(new[]{10}));
        server.Processes[1].Name="docker";called.Clear();result=ProcessControl.StopDetailed(server,partial);
        Check("protected child is skipped with a recorded reason count",result.ListenerStopped&&result.Skipped==1&&result.Errors.Count==0&&called.SequenceEqual(new[]{10}));
        server.Processes[0].Name="wmux";called.Clear();result=ProcessControl.StopDetailed(server,partial);
        Check("protected root is refused even with incorrect server flag",!result.ListenerStopped&&result.Errors.Count==1&&called.Count==0);
    }
    static Process StartFixture(){
        var p=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--fixture"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=temp});
        return p;
    }
    static string Ready(Process p){Task<string> read=p.StandardOutput.ReadLineAsync();if(!read.Wait(8000))throw new Exception("Fixture timed out.");if(String.IsNullOrEmpty(read.Result))throw new Exception("Fixture failed: "+p.StandardError.ReadToEnd());return read.Result;}
    static void Integration(){
        using(var first=StartFixture())using(var second=StartFixture()){
            try {
                int[] ports=Ready(first).Split(',').Select(Int32.Parse).ToArray();Ready(second);
                Check("fixtures created independently",first.Id!=second.Id);
                Check("same-user ownership verified",Native.IsOwned(first.Id));
                DateTime sampledStart;double sampledCpu;long sampledMemory;
                Check("query-only native sampling succeeds",Native.SampleProcess(first.Id,out sampledStart,out sampledCpu,out sampledMemory));
                first.Refresh();
                Check("native metrics agree with Framework for the owned fixture",sampledStart.Kind==DateTimeKind.Utc&&Native.SameStart(sampledStart,first.StartTime.ToUniversalTime())&&sampledMemory>=16*1024*1024&&Math.Abs(sampledMemory-first.WorkingSet64)<4*1024*1024&&Math.Abs(sampledCpu-first.TotalProcessorTime.TotalMilliseconds)<100);
                Check("unavailable process cannot provide a valid sample",!Native.SampleProcess(0,out sampledStart,out sampledCpu,out sampledMemory)&&sampledMemory==0&&sampledCpu==0&&sampledStart==default(DateTime));
                Check("cwd resolves without reading environment",String.Equals(Native.CurrentDirectory(first.Id).TrimEnd('\\'),temp.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase));
                var sockets=Native.Sockets();Check("native IPv4 listener detected",sockets.Any(s=>s.Pid==first.Id&&s.Port==ports[0]&&s.State==2));
                Check("native IPv6 listener detected",sockets.Any(s=>s.Pid==first.Id&&s.Port==ports[1]&&s.State==2&&s.Address=="::1"));
                var scanner=new Scanner();var config=new Settings();var snap=scanner.Scan(config);var server=snap.Servers.Single(s=>s.Processes[0].Pid==first.Id);
                Check("multiple ports group into one service",server.Ports.Count==2&&snap.Servers.Count(s=>s.Processes[0].Pid==first.Id)==1);
                Check("working set and total RAM measured",server.Memory>0&&snap.TotalMemory>snap.FreeMemory&&snap.FreeMemory>0);
                Check("live uptime from process creation",server.Started<=DateTime.UtcNow&&DateTime.UtcNow-server.Started<TimeSpan.FromMinutes(1));
                Thread.Sleep(300);snap=scanner.Scan(config);server=snap.Servers.Single(s=>s.Processes[0].Pid==first.Id);Check("CPU/history samples collected",server.History.Count==2&&server.Cpu>=0&&server.Cpu<=100);
                using(var client=new TcpClient()) {
                    client.Connect(IPAddress.Loopback,ports[0]);Thread.Sleep(100);snap=scanner.Scan(config);server=snap.Servers.Single(s=>s.Processes[0].Pid==first.Id);Check("established inbound connection measured",server.Connections>=1);
                    int selfPid=Process.GetCurrentProcess().Id;
                    Check("live TCP client identifies current process without adding stop targets",server.Clients.Any(c=>c.Pid==selfPid&&c.Started!=default(DateTime)&&c.Connections>=1)&&!server.Processes.Any(p=>p.Pid==selfPid));
                }
                using(var client=new TcpClient(AddressFamily.InterNetworkV6)){
                    client.Connect(IPAddress.IPv6Loopback,ports[1]);Thread.Sleep(100);snap=scanner.Scan(config);server=snap.Servers.Single(s=>s.Processes[0].Pid==first.Id);
                    Check("live IPv6 endpoints identify the local client",server.Clients.Any(c=>c.Pid==Process.GetCurrentProcess().Id));
                }
                var node=server.Processes[0];var stale=new ProcessNode{Pid=node.Pid,Name=node.Name,Started=node.Started.AddSeconds(-1)};string error;
                Check("real process PID mismatch refuses termination",!Native.Stop(stale,out error)&&!first.HasExited);
                var self=new ProcessNode{Pid=Process.GetCurrentProcess().Id,Name="wtp-tests",Started=Process.GetCurrentProcess().StartTime.ToUniversalTime()};Check("own process cannot be stopped",!Native.Stop(self,out error));
                server.Protected=true;Check("protected server stop refused",ProcessControl.Stop(server).Count>0&&!first.HasExited);server.Protected=false;
                server.SampledAt=DateTime.UtcNow.AddMinutes(-1);Check("stale process stop refused",ProcessControl.Stop(server).Count>0&&!first.HasExited);server.SampledAt=DateTime.UtcNow;
                Check("owned test server stops",ProcessControl.Stop(server).Count==0);
                Check("stop waits for exit before immediate refresh",!scanner.Scan(config).Servers.Any(s=>s.Key==server.Key));
                first.WaitForExit(3000);Check("fixture actually exited",first.HasExited);
                Check("unselected fixture preserved",!second.HasExited);Check("stopped listener disappeared",!Native.Sockets().Any(s=>s.State==2&&s.Pid==first.Id));
                Check("scanner removes exited server",!scanner.Scan(config).Servers.Any(s=>s.Key==server.Key));
                var timer=Stopwatch.StartNew();for(int i=0;i<5;i++)scanner.Scan(config);timer.Stop();Console.WriteLine("INFO 5 native scans: "+timer.ElapsedMilliseconds+" ms");Check("native scan cycle under 3 seconds",timer.ElapsedMilliseconds<3000);
            }finally{foreach(var p in new[]{first,second})if(!p.HasExited){p.Kill();p.WaitForExit(3000);}}
        }
    }
    static int Fixture(){
        var ipv4=new TcpListener(IPAddress.Loopback,0);var ipv6=new TcpListener(IPAddress.IPv6Loopback,0);ipv6.Server.DualMode=false;
        ipv4.Start();ipv6.Start();byte[] memory=new byte[16*1024*1024];for(int i=0;i<memory.Length;i+=4096)memory[i]=1;
        Console.WriteLine(((IPEndPoint)ipv4.LocalEndpoint).Port+","+((IPEndPoint)ipv6.LocalEndpoint).Port);Console.Out.Flush();
        // Bounded lifetime also protects test hosts if their parent test runner is interrupted.
        DateTime end=DateTime.UtcNow.AddSeconds(40);while(DateTime.UtcNow<end)Thread.Sleep(100);GC.KeepAlive(memory);ipv4.Stop();ipv6.Stop();return 0;
    }
}
