using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
[assembly: AssemblyVersion(WhatThePort.AppVersion.Current)]
[assembly: AssemblyFileVersion(WhatThePort.AppVersion.Current)]

namespace WhatThePort.Cli {
    public static class Program {
        public static int Main(string[] args) {
            try {
                if(args.Length==0){Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"WhatThePort.exe")){UseShellExecute=true});return 0;}
                if(args[0]=="--help"||args[0]=="help"){Help();return 0;}
                var store=new Store(null);var settings=store.LoadSettings();
                if(args[0]=="list") {
                    if(args.Skip(1).Any(a=>a!="--json"&&a!="--all"))throw new ArgumentException("Usage: wtp list [--json] [--all]");
                    if(args.Contains("--all"))settings.AllListeners=true;
                    var scanner=new Scanner();scanner.Scan(settings);Thread.Sleep(250);var snapshot=scanner.Scan(settings);
                    if(args.Contains("--json"))Console.WriteLine(Store.Json(snapshot));
                    else{Console.WriteLine("PORT    MEMORY       CPU      UPTIME  PROJECT");foreach(var s in snapshot.Servers)Console.WriteLine("{0,-7} {1,-12} {2,-8} {3,-7} {4}{5}",":"+s.Port,Format.Bytes(s.Memory),Format.Percent(s.Cpu),Format.Duration(DateTime.UtcNow-s.Started),s.Name,s.Protected?" [protected]":"");Console.WriteLine("\n{0} server(s) · {1} RAM · {2} free",snapshot.Servers.Count,Format.Total(snapshot.ServerMemory),Format.Total(snapshot.FreeMemory));}return 0;
                }
                if(args[0]=="link") {
                    string[] allowed={"--port","--folder","--agent","--session","--preview"};
                    for(int i=1;i<args.Length;i+=2)if(!allowed.Contains(args[i])||i+1>=args.Length)throw new ArgumentException("Usage: wtp link --port PORT --folder PATH [--agent codex|claude] [--session ID] [--preview URL]");
                    string agent=Option(args,"--agent","");
                    if(agent!=""&&agent!="codex"&&agent!="claude")throw new ArgumentException("Agent must be codex or claude.");
                    var link=new ProjectLink { Port=Int32.Parse(Option(args,"--port","0"),CultureInfo.InvariantCulture),Folder=Path.GetFullPath(Option(args,"--folder",Environment.CurrentDirectory)),Agent=agent=="codex"?"Codex":agent=="claude"?"Claude Code":"",Session=Option(args,"--session",""),Preview=Option(args,"--preview","") };
                    store.SaveLink(link);Console.WriteLine("Saved project links for :"+link.Port);return 0;
                }
                throw new ArgumentException("Unknown command. Run wtp --help.");
            }catch(Exception e){Console.Error.WriteLine("wtp: "+e.Message);return 1;}
        }
        static string Option(string[] args,string name,string fallback){int i=Array.IndexOf(args,name);return i<0?fallback:i+1<args.Length?args[i+1]:fallback;}
        static void Help(){Console.WriteLine("What the Port for Windows "+AppVersion.Current+"\n\n  wtp                    Open the tray panel\n  wtp list               Show development servers\n  wtp list --json         Machine-readable snapshot\n  wtp list --all          Include all current-user listeners\n  wtp link --port 3000 --folder C:\\dev\\app --agent codex --session ID\n  wtp link --port 3000 --folder C:\\dev\\app --preview https://app.vercel.app\n\nUse the tray panel to review and stop processes. No admin rights required.");}
    }
}
