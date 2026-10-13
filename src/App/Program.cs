using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace WhatThePort.App {
    public static class Program {
        [STAThread] public static int Main(string[] args){
            // Set WPF accessibility behavior before constructing any UI, including without an exe.config.
            for(int version=1;version<=4;version++)AppContext.SetSwitch("Switch.UseLegacyAccessibilityFeatures"+(version==1?"":"."+version),false);
#if SINGLE_FILE
            if(args.Contains("--licenses"))return LicenseViewer.Show();
#endif
            bool snapshot=args.Contains("--snapshot"),smoke=args.Contains("--smoke-test"),demo=snapshot||args.Contains("--demo");
            int smokeExit=0;
            string instance="Local\\WhatThePort.Windows"+(smoke?".Smoke."+Guid.NewGuid().ToString("N"):demo?".Demo":"");
            // Isolated, demo-only namespace for the two-process activation regression test.
            int instanceIndex=Array.IndexOf(args,"--test-instance");Guid testInstance;
            if(instanceIndex>=0){if(!demo||instanceIndex+1>=args.Length||!Guid.TryParse(args[instanceIndex+1],out testInstance))return 2;instance="Local\\WhatThePort.Windows.Test."+testInstance.ToString("N");}
            bool first=true;using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,instance+".Show"))using(var mutex=new Mutex(true,instance,out first)){
                if(!first&&!snapshot){if(!args.Contains("--background")){InstanceActivation.AllowExistingInstanceForeground();signal.Set();}return 0;}
                try{
                    var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                    app.DispatcherUnhandledException+=delegate(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e){MessageBox.Show(e.Exception.Message,"What the Port",MessageBoxButton.OK,MessageBoxImage.Error);e.Handled=true;};
                    int languageIndex=Array.IndexOf(args,"--language");string language=languageIndex>=0&&languageIndex+1<args.Length?args[languageIndex+1]:null;
                    var panel=new Panel(demo,snapshot,smoke?Path.Combine(Path.GetTempPath(),"wtp-smoke-"+Guid.NewGuid().ToString("N")):null,language);
                    var registration=ThreadPool.RegisterWaitForSingleObject(signal,delegate{app.Dispatcher.BeginInvoke(new Action(()=>panel.ShowFromLaunch()));},null,Timeout.Infinite,false);
                    if(smoke){int i=Array.IndexOf(args,"--smoke-test");if(i+1>=args.Length)throw new ArgumentException("--smoke-test requires a report path.");app.Dispatcher.BeginInvoke(new Action(async delegate{smokeExit=await panel.LiveSmoke(args[i+1])?0:1;}));}
                    else if(snapshot){int i=Array.IndexOf(args,"--snapshot");if(i+1>=args.Length)throw new ArgumentException("--snapshot requires a directory.");app.Dispatcher.BeginInvoke(new Action(()=>panel.CaptureAll(Path.GetFullPath(args[i+1]))));}
                    else{if(!args.Contains("--background"))panel.ShowFromLaunch();panel.Start();}
                    app.Run();registration.Unregister(null);return smokeExit;
                }catch(Exception e){if(snapshot){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"snapshot-error.log"),e.ToString());}else MessageBox.Show(e.Message,"What the Port could not start");return 1;}
            }
        }
    }
}
