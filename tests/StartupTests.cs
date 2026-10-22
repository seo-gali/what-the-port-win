using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WhatThePort;
using Panel=WhatThePort.App.Panel;

// Exercise the production startup order without loading WinForms or entering
// the dispatcher first. Demo startup skips scanning and cannot catch this bug.
public static class StartupTests {
    static readonly FieldInfo Current=typeof(Panel).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly FieldInfo Scanning=typeof(Panel).GetField("scanning",BindingFlags.Instance|BindingFlags.NonPublic);
    [STAThread] public static int Main(string[] args){
        if(args.Length!=2||(args[0]!="visible"&&args[0]!="background"))return 2;
        AppDomain.CurrentDomain.UnhandledException+=delegate(object sender,UnhandledExceptionEventArgs e){
            Console.Error.WriteLine("FAIL unhandled startup exception: "+e.ExceptionObject);Environment.Exit(1);
        };
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        Panel panel=null;int exitCode=0;var listener=new TcpListener(IPAddress.Loopback,0);var allocations=new List<byte[]>();
        var observer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e){
            Console.Error.WriteLine("FAIL dispatcher startup exception: "+e.Exception);exitCode=1;e.Handled=true;app.Shutdown();
        };
        try{
            listener.Start();int fixturePid=Process.GetCurrentProcess().Id;
            new Store(args[1]).SaveSettings(new Settings{Cleanup="Off",Notifications=false,ScanSeconds=3,AllListeners=true});
            panel=new Panel(false,false,args[1],"en");
            bool background=args[0]=="background";
            if(!background)Open(panel);
            if(SynchronizationContext.Current!=null)throw new InvalidOperationException("Startup fixture already has a synchronization context.");
            var elapsed=Stopwatch.StartNew();DateTime lastScan=DateTime.MinValue;int scans=0;bool opened=!background,detailOpened=false;
            observer.Tick+=delegate{
                try{
                    if(elapsed.Elapsed.TotalSeconds>12)throw new InvalidOperationException("Startup did not complete live scans in time.");
                    if((bool)Scanning.GetValue(panel))return;
                    var snapshot=(Snapshot)Current.GetValue(panel);
                    if(snapshot.Error!=null)throw new InvalidOperationException(snapshot.Error);
                    if(snapshot.TotalMemory<=0){if(elapsed.Elapsed.TotalSeconds>2.5)throw new InvalidOperationException("The initial scan was not completed before the periodic scan.");return;}
                    if(snapshot.At!=lastScan){
                        lastScan=snapshot.At;scans++;
                        if(scans<3){var allocation=new byte[8*1024*1024];for(int i=0;i<allocation.Length;i+=4096)allocation[i]=1;allocations.Add(allocation);}
                    }
                    if(!opened){
                        if(panel.Window!=null)throw new InvalidOperationException("Background startup created a panel before opening.");
                        if(scans<2)return;
                        Open(panel);opened=true;
                    }
                    if(!panel.IsVisible)throw new InvalidOperationException("The startup panel disappeared.");
                    var server=snapshot.Servers.Single(s=>s.Processes[0].Pid==fixturePid);
                    if(!detailOpened){
                        typeof(Panel).GetField("detailKey",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(panel,server.Key);
                        typeof(Panel).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(panel,new object[]{"detail"});detailOpened=true;
                    }
                    if(scans>=3){
                        var charts=((StackPanel)panel.Window.FindName("Body")).Children.OfType<WhatThePort.App.Chart>().ToList();
                        if(server.History.Count<3||server.History.Max(s=>s.Memory)-server.History.Min(s=>s.Memory)<4*Format.MB)throw new InvalidOperationException("The controlled listener did not accumulate changing measurements.");
                        if(charts.Count!=2||charts.Any(c=>c.Samples.Count!=server.History.Count||c.Samples.Last().At!=server.History.Last().At||c.Samples.Last().Memory!=server.Memory))throw new InvalidOperationException("Visible charts did not receive the latest periodic measurements.");
                        Console.WriteLine("PASS live charts: "+args[0]+"; timer adds changing listener measurements to both visible charts");
                        Console.WriteLine("PASS real startup: "+args[0]+"; initial and periodic scans succeeded; panel remains visible");
                        observer.Stop();panel.Quit();
                    }
                }catch(Exception e){Console.Error.WriteLine("FAIL real startup: "+e);exitCode=1;observer.Stop();panel.Quit();}
            };
            // Match Program.Main: start monitoring before Application.Run.
            panel.Start();observer.Start();app.Run();
        }catch(Exception e){Console.Error.WriteLine("FAIL real startup: "+e);exitCode=1;}
        finally{observer.Stop();listener.Stop();GC.KeepAlive(allocations);if(!app.Dispatcher.HasShutdownStarted){if(panel!=null)panel.Quit();else app.Shutdown();}}
        return exitCode;
    }
    static void Open(Panel panel){
        panel.ShowFromLaunch();panel.Window.Opacity=0;
        ((Button)panel.Window.FindName("Pin")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
