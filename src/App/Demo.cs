using System;
using System.Collections.Generic;

namespace WhatThePort.App {
    public static class Demo {
        public static ProjectLink LinkFor(Server server){return server.Key=="demo-0"?new ProjectLink{Folder=server.Folder,Port=server.Port,Agent="Codex",Session="demo-session",Preview="https://demo.vercel.app"}:null;}
        public static Snapshot Create() {
            DateTime now=DateTime.UtcNow; var result=new Snapshot { At=now,TotalMemory=16*Format.GB,FreeMemory=(long)(4.2*Format.GB) };
            string[] names={"menubar port monitor","main","feat/export svg","tokens v2","fix/auth timeout"};
            string[] projects={"what-the-port","tomjohn.design","paper-plugins","token-studio","old-worktree"};
            int[] ports={3000,3001,5173,6006,8000}; long[] memory={(long)(1.21*Format.GB),612*Format.MB,184*Format.MB,(long)(2.74*Format.GB),96*Format.MB};
            for(int i=0;i<5;i++) {
                var server=new Server { Key="demo-"+i,Port=ports[i],Ports=new List<int>{ports[i]},Address="127.0.0.1",Name=names[i],Folder=@"C:\dev\"+projects[i],Branch=i==1?"main":names[i].Replace(" ","-"),Agent=i==0?"Codex":i==2?"Claude Code":null,Started=now.AddHours(i==4?-48:i==1?-24:-3),Observed=now.AddHours(-6),LastActive=now.AddHours(i==2?-5:i==4?-12:0),SampledAt=now,Memory=memory[i],Cpu=i==3?5.4:0.1,Warning=i==3?"+1.1 GB in 10 min":null,Connections=i==0?2:0 };
                for(int n=0;n<=60;n++) {
                    double factor=i==3?.6+.4*n/60.0:i==0?.94+.06*n/60.0:i==1?.997+Math.Sin(n*2)*.003:1;
                    server.History.Add(new Sample{At=now.AddSeconds(-600+n*10),Memory=(long)(memory[i]*factor),Cpu=i==3?2+Math.Pow(Math.Sin(n*.37),8)*22:n==46?24:server.Cpu});
                }
                server.Processes.Add(new ProcessNode{Pid=12000+i*100,Name=i==2?"python":"node",Started=server.Started,Memory=memory[i]*3/4,Cpu=server.Cpu});
                server.Processes.Add(new ProcessNode{Pid=12001+i*100,Name="worker",Started=server.Started,Memory=memory[i]/4,Depth=1});
                if(i==0)server.Clients.Add(new ConnectedApp{Pid=15000,Started=now.AddHours(-2),Name="node",Project="token-studio",Agent="Codex",Connections=2});
                result.Servers.Add(server);
            }
            return result;
        }
    }
}
