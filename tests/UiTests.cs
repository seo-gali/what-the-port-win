using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WhatThePort.App;

public static class UiTests {
    static int passed,failed;
    static WhatThePort.App.Panel panel;
    static Window window;
    [STAThread] public static int Main(){
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        try{
            PlacementTests();
            panel=new WhatThePort.App.Panel(true,true,null,"en");window=panel.Window;window.Opacity=0;window.ShowActivated=false;window.Show();
            NativePlacementTests();
            var bounds=PanelPlacement.Calculate(PanelPlacement.WorkArea(window),new Size(440,594),null);
            window.Left=bounds.Left;window.Top=bounds.Top;window.Width=bounds.Width;window.Height=bounds.Height;
            double bottom=bounds.Bottom;
            // Exercise actual WPF templates invisibly; every operation uses isolated demo data.
            Layout();Check("list starts with five servers",Rows().Count()==5);
            HeaderTests();
            NavigationLayoutTests();
            ResponsiveSizingTests();
            ChartRangeTests();
            ToolTipTests();
            KeyboardTests();
            CheckNames("list controls have screen reader names");
            Click(Rows().First());Check("row opens server detail",Title()=="menubar port monitor");
            CheckPlacement("detail grows upward with visible footer",bottom);
            Check("detail has memory and CPU charts",All(Body()).OfType<Chart>().Count()==2);
            CaptureVisual((FrameworkElement)window.Content,"detail-compact");
            ProcessDetailsLoadingTests();
            CheckNames("detail controls have screen reader names");
            Check("window title identifies current screen",window.Title.Contains(Title()));
            Click(ByName("Open local server (O)"));Check("demo blocks external URL",Message().Contains("simulated"));
            Click(ByName("Open project in terminal (T)"));Check("demo blocks terminal launch",Message().Contains("terminal"));
            Click(ByName("Resume linked agent session (A)"));Check("demo blocks agent launch",Message().Contains("session"));
            Click(ByName("Open Vercel preview (V)"));Check("demo blocks preview launch",Message().Contains("preview"));
            Click(ByName("Manage project links and copy local URL"));Check("project links screen opens",Title()=="Project links");
            CheckNames("link fields and buttons have screen reader names");
            CheckPlacement("links preserve bottom and footer",bottom);
            Click(ByName("Save project links"));Check("demo links save returns to detail",Title()=="menubar port monitor"&&Message().Contains("simulated"));
            CheckPlacement("detail return preserves bottom",bottom);
            Click((Button)window.FindName("Back"));Click(ByName("Select servers to stop (C)"));Check("cleanup view opens",Title()=="Clean up");
            CheckNames("cleanup controls have screen reader names");
            CheckPlacement("cleanup preserves bottom and footer",bottom);
            var checks=All(Body()).OfType<CheckBox>().ToList();Check("idle cleanup candidates preselected",checks.Count(c=>c.IsChecked==true)==2);
            var first=checks.First();first.IsChecked=true;Click(first);Check("cleanup selection updates stop count",ByName("Stop selected server process trees").Content.ToString().StartsWith("Stop 3 servers"));
            Click(ByName("Stop selected server process trees"));Check("demo cleanup removes only selection",Title()=="What the Port"&&Rows().Count()==2&&Message().Contains("No processes"));
            Click(ByName("Settings (Ctrl+,)"));Check("settings view opens",Title()=="Settings");
            CheckNames("settings controls have screen reader names");
            CheckPlacement("settings fit work area with visible save button",bottom);
            var scroll=(ScrollViewer)window.FindName("Scroll");scroll.ApplyTemplate();var scrollbar=ScrollBarFrom(scroll);Check("compact scrollbar width",scrollbar!=null&&scrollbar.Width==5);
            Check("long settings content scrolls within panel",scroll.ScrollableHeight>0);
            var sample=All(Body()).OfType<TextBox>().First();sample.Text="0";Click(ByName("Save preferences"));Check("invalid preferences stay on settings",Title()=="Settings"&&Message().Contains("thresholds"));
            var notice=(TextBlock)window.FindName("Message");Check("error text exposed as screen reader live region",AutomationProperties.GetLiveSetting(notice)==AutomationLiveSetting.Polite&&UIElementAutomationPeer.CreatePeerForElement(notice).GetName().Contains("thresholds"));
            sample.Text="5";Click(ByName("Save preferences"));Check("valid preferences applied",Title()=="What the Port"&&((TextBlock)window.FindName("Status")).Text.Contains("DEMO"));
            CheckPlacement("return to list preserves bottom",bottom);
            Click(ByName("Select servers to stop (C)"));foreach(var check in All(Body()).OfType<CheckBox>().ToList()){if(check.IsChecked!=true){check.IsChecked=true;Click(check);}}
            // Every click re-renders the view, so select remaining current controls by iteration.
            for(int i=0;i<5;i++){var uncheckedBox=All(Body()).OfType<CheckBox>().FirstOrDefault(c=>c.IsChecked!=true);if(uncheckedBox==null)break;uncheckedBox.IsChecked=true;Click(uncheckedBox);}
            Click(ByName("Stop selected server process trees"));Check("empty state after final cleanup",Rows().Count()==0&&All(Body()).OfType<TextBlock>().Any(t=>t.Text=="A little breathing room."));
            var pin=(Button)window.FindName("Pin");Click(pin);Check("pin toggles",pin.Content.ToString()=="◆");
            Check("pin state has a meaningful accessible name",UIElementAutomationPeer.CreatePeerForElement(pin).GetName()=="Unpin panel");
            LocalizationTests();
            LaunchVisibilityTests();
            TrayLifetimeTests();
        }catch(Exception e){failed++;Console.WriteLine("FAIL UI exception: "+e);}
        finally{if(panel!=null)panel.Quit();else app.Shutdown();}
        Console.WriteLine("\n"+passed+" UI passed, "+failed+" failed");return failed==0?0:1;
    }
    const BindingFlags PrivateInstance=BindingFlags.NonPublic|BindingFlags.Instance;
    static double ChartVariation(Chart chart){
        double width=chart.Spark?38:360;chart.Height=chart.Spark?20:112;chart.Measure(new Size(width,chart.Height));chart.Arrange(new Rect(0,0,width,chart.Height));
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())typeof(Chart).GetMethod("OnRender",PrivateInstance).Invoke(chart,new object[]{drawing});
        var line=visual.Drawing.Children.OfType<GeometryDrawing>().Single(d=>d.Pen!=null&&d.Geometry is StreamGeometry);
        return line.Geometry.GetRenderBounds(line.Pen).Height-line.Pen.Thickness;
    }
    static void ChartRangeTests(){
        var now=DateTime.UtcNow;
        var history=new List<WhatThePort.Sample>{new WhatThePort.Sample{At=now.AddSeconds(-6),Memory=67*WhatThePort.Format.MB,Cpu=.1},new WhatThePort.Sample{At=now.AddSeconds(-3),Memory=68*WhatThePort.Format.MB,Cpu=.2},new WhatThePort.Sample{At=now,Memory=67*WhatThePort.Format.MB,Cpu=.1}};
        foreach(bool spark in new[]{false,true})foreach(bool cpu in new[]{false,true}){
            var chart=new Chart{Samples=history,Spark=spark,Cpu=cpu,Threshold=spark?0:2*WhatThePort.Format.GB};double variation=ChartVariation(chart);
            Console.WriteLine("INFO "+(spark?"list":"detail")+" "+(cpu?"CPU":"memory")+" painted variation="+variation.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture)+" DIP");
            Check((spark?"list":"detail")+" "+(cpu?"CPU":"memory")+" shows small measured changes",variation>(spark?5:8));
            chart.Samples=history.Select(s=>new WhatThePort.Sample{At=s.At,Memory=67*WhatThePort.Format.MB,Cpu=.1}).ToList();variation=ChartVariation(chart);
            Check((spark?"list":"detail")+" "+(cpu?"CPU":"memory")+" keeps unchanged values flat",Math.Abs(variation)<.01);
        }
        Invoke("Navigate","detail");var server=((WhatThePort.Snapshot)Field("current")).Servers.First();
        var previous=All(Body()).OfType<Chart>().First().Samples.Count;var sample=new WhatThePort.Sample{At=server.History.Last().At.AddSeconds(3),Memory=server.Memory+WhatThePort.Format.MB,Cpu=.2};
        server.History.Add(sample);server.Memory=sample.Memory;server.Cpu=sample.Cpu;Rerender();
        Check("visible charts receive the newest measurements after refresh",All(Body()).OfType<Chart>().All(c=>c.Samples.Count==previous+1&&c.Samples.Last()==sample));
        server.History.Remove(sample);server.Memory=server.History.Last().Memory;server.Cpu=server.History.Last().Cpu;Invoke("Navigate","list");
    }
    static Rect Displayed(FrameworkElement element){return element.TransformToAncestor(window).TransformBounds(new Rect(new Point(),element.RenderSize));}
    static void ResponsiveSizingTests(){
        var original=PanelPlacement.PhysicalBounds(window);var work=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).WorkingArea;
        double textHeight=Displayed(All(Rows().First()).OfType<TextBlock>().First(t=>t.Text=="menubar port monitor")).Height;
        var spark=All(Rows().First()).OfType<Chart>().Single();double sparkWidth=Displayed(spark).Width;int samples=spark.Samples.Count;
        double scale=original.Height/window.ActualHeight;
        try{
            PanelPlacement.Place(window,594,new Point(work.Left+40,work.Top+24*scale+original.Height),true,false);Layout();Invoke("RememberPosition");
            double top=PanelPlacement.PhysicalBounds(window).Top;
            Click((Button)window.FindName("Larger"));Click((Button)window.FindName("Larger"));
            Check("top-positioned panel keeps top during consecutive growth",Math.Abs(PanelPlacement.PhysicalBounds(window).Top-top)<=1);
            var name=All(Rows().First()).OfType<TextBlock>().First(t=>t.Text=="menubar port monitor");spark=All(Rows().First()).OfType<Chart>().Single();
            Check("large panel enlarges text and gives graphs more space",Displayed(name).Height>textHeight*1.08&&Displayed(spark).Width>sparkWidth*2&&spark.Samples.Count==samples);
            CaptureVisual((FrameworkElement)window.Content,"list-large");
            for(int i=0;i<3;i++)Click((Button)window.FindName("Smaller"));
            name=All(Rows().First()).OfType<TextBlock>().First(t=>t.Text=="menubar port monitor");
            Check("minimum panel and text are smaller than previous minimum",window.Width<396&&window.Height<550&&Displayed(name).Height<textHeight*.95);
            Check("top stays fixed when shrinking",Math.Abs(PanelPlacement.PhysicalBounds(window).Top-top)<=1);
            CaptureVisual((FrameworkElement)window.Content,"list-small");
            Click(ByName("Settings (Ctrl+,)"));var input=All(Body()).OfType<TextBox>().First();input.Text="unfinished";
            for(int i=0;i<3;i++)Click((Button)window.FindName("Larger"));
            Check("resizing preserves incomplete form input",All(Body()).OfType<TextBox>().First()==input&&input.Text=="unfinished");
            CheckScaledPopup("large");
            for(int i=0;i<3;i++)Click((Button)window.FindName("Smaller"));
            Check("small form fields retain a 30 DIP hit area",All(Body()).OfType<TextBox>().All(t=>Displayed(t).Height>=29.5));
            Check("small settings switches and sliders retain a 30 DIP hit area",All(Body()).OfType<Control>().Where(c=>c is Slider||c is System.Windows.Controls.Primitives.ToggleButton).All(c=>Displayed(c).Height>=29.5));
            CheckScaledPopup("small");
            CaptureVisual((FrameworkElement)window.Content,"settings-small");
            var footer=(FrameworkElement)window.FindName("Footer");Check("small forms keep footer visible",Displayed(footer).Bottom<=window.ActualHeight&&Displayed(footer).Top>0);
            Invoke("Navigate","list");Click((Button)window.FindName("Larger"));
            PanelPlacement.Place(window,594,null,true,false);Layout();Invoke("RememberPosition");double bottom=PanelPlacement.PhysicalBounds(window).Bottom;
            Click((Button)window.FindName("Larger"));Click((Button)window.FindName("Larger"));Click((Button)window.FindName("Smaller"));Click((Button)window.FindName("Smaller"));
            Check("moving down reselects bottom anchoring",Math.Abs(PanelPlacement.PhysicalBounds(window).Bottom-bottom)<=1);
        }finally{
            Invoke("Navigate","list");while(((WhatThePort.Settings)Field("settings")).PanelSize>1)Click((Button)window.FindName("Smaller"));while(((WhatThePort.Settings)Field("settings")).PanelSize<1)Click((Button)window.FindName("Larger"));
            PanelPlacement.Place(window,594,new Point(original.Left,original.Bottom),true,false);Layout();Invoke("RememberPosition");
        }
    }
    static void CheckScaledPopup(string size){
        var select=All(Body()).OfType<ComboBox>().First();select.IsDropDownOpen=true;Layout();Pump();
        try{
            var popup=(System.Windows.Controls.Primitives.Popup)select.Template.FindName("PART_Popup",select);var child=(FrameworkElement)popup.Child;child.UpdateLayout();
            double popupWidth=child.PointToScreen(new Point(child.ActualWidth,0)).X-child.PointToScreen(new Point()).X;
            double selectWidth=select.PointToScreen(new Point(select.ActualWidth,0)).X-select.PointToScreen(new Point()).X;
            if(Math.Abs(popupWidth-selectWidth)>2)Console.WriteLine("INFO "+size+" dropdown="+popupWidth+" field="+selectWidth+" child="+child.ActualWidth);
            Check(size+" dropdown matches scaled field width",popup.IsOpen&&Math.Abs(popupWidth-selectWidth)<=2);
        }finally{select.IsDropDownOpen=false;Pump();}
    }
    static void NavigationLayoutTests(){
        Invoke("Place",false,false);Layout();
        Rect initial=PanelPlacement.PhysicalBounds(window);
        var summary=(StackPanel)window.FindName("Summary");var summaryAt=summary.TranslatePoint(new Point(),window);
        var scroll=(ScrollViewer)window.FindName("Scroll");scroll.ScrollToBottom();Layout();
        Check("memory and CPU summary stay fixed while server rows scroll",summary.TranslatePoint(new Point(),window)==summaryAt&&scroll.VerticalOffset>0);scroll.ScrollToTop();
        Check("server rows have no redundant hover tooltips",Rows().All(r=>r.ToolTip==null&&All(r).OfType<FrameworkElement>().All(e=>e.ToolTip==null)));
        Click(Rows().First());
        Check("detail keeps list size and position",PanelPlacement.PhysicalBounds(window)==initial);
        var expander=All(Body()).OfType<Expander>().Single();
        var chart=All(Body()).OfType<Chart>().First();
        Check("process details appear beside project information before charts",expander.TranslatePoint(new Point(),Body()).Y<chart.TranslatePoint(new Point(),Body()).Y);
        Click(ByName("Manage project links and copy local URL"));
        Check("links keep list size and position",PanelPlacement.PhysicalBounds(window)==initial);
        Click((Button)window.FindName("Back"));
        Check("links Back returns to the originating detail",Title()=="menubar port monitor");
        Invoke("Navigate","list");Click(ByName("Settings (Ctrl+,)"));
        Check("settings keep list size and position",PanelPlacement.PhysicalBounds(window)==initial);
        var inputs=All(Body()).OfType<TextBox>().Take(4).ToArray();
        var a=inputs[0].TranslatePoint(new Point(),Body());var second=inputs[1].TranslatePoint(new Point(),Body());
        var c=inputs[2].TranslatePoint(new Point(),Body());var d=inputs[3].TranslatePoint(new Point(),Body());
        Check("numeric settings use two equal-width fields per row",Near(a.Y,second.Y)&&second.X>a.X&&Near(c.Y,d.Y)&&c.Y>a.Y&&Near(inputs[0].ActualWidth,inputs[1].ActualWidth));
        Check("settings offer Windows login startup toggle",All(Body()).OfType<System.Windows.Controls.Primitives.ToggleButton>().Any(t=>(string)t.Tag=="startup"));
        Check("header offers smaller and larger buttons",window.FindName("Smaller") is Button&&window.FindName("Larger") is Button);
        inputs[0].Text="9";
        var startup=All(Body()).OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(t=>(string)t.Tag=="startup");startup.IsChecked=true;
        var opacity=All(Body()).OfType<Slider>().Single();opacity.Value=60;Layout();
        Check("opacity preview affects only background",Near(((Border)window.FindName("PanelBackground")).Background.Opacity,.6)&&Near(window.Opacity,0)&&Near(inputs[0].Opacity,1));
        Invoke("ToggleSnooze");
        Check("snooze toggles on with visible state without losing draft input",(DateTime)Field("snoozeUntil")>DateTime.UtcNow&&Message().Contains("snoozed")&&inputs[0].Text=="9"&&All(Body()).OfType<Button>().Any(b=>b.Content as string=="Resume alerts"));
        Invoke("ToggleSnooze");Check("snooze toggles off immediately",(DateTime)Field("snoozeUntil")<DateTime.UtcNow&&Message().Contains("resumed")&&All(Body()).OfType<TextBlock>().Any(t=>t.Text=="Alerts active"));
        Click(ByName("Discard changes"));Click(ByName("Settings (Ctrl+,)"));
        Check("cancel discards startup and opacity previews",All(Body()).OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(t=>(string)t.Tag=="startup").IsChecked==false&&Near(((Border)window.FindName("PanelBackground")).Background.Opacity,1));
        Invoke("Navigate","list");
        Click((Button)window.FindName("Larger"));var larger=PanelPlacement.PhysicalBounds(window);
        Check("plus enlarges panel while preserving bottom",larger.Width>initial.Width&&larger.Height>=initial.Height&&Near(larger.Bottom,initial.Bottom));
        Click(Rows().First());Check("navigation preserves user-selected size",PanelPlacement.PhysicalBounds(window)==larger);
        Click(ByName("Manage project links and copy local URL"));Press(Key.Escape);Check("links Escape returns to server detail",Title()=="menubar port monitor");
        Invoke("Navigate","list");Click((Button)window.FindName("Smaller"));var smaller=PanelPlacement.PhysicalBounds(window);Check("minus restores original size and keeps bottom",Near(smaller.Width,initial.Width)&&Near(smaller.Height,initial.Height)&&Near(smaller.Bottom,larger.Bottom));
        typeof(WhatThePort.App.Panel).GetField("stopDetails",PrivateInstance).SetValue(panel,"Test server stopped.");
        var dialog=(Window)typeof(WhatThePort.App.Panel).GetMethod("StopDetailsWindow",PrivateInstance).Invoke(panel,null);
        try{var text=All(dialog).OfType<TextBox>().Single();Check("stop result window uses app theme and offers close",text.IsReadOnly&&((SolidColorBrush)text.Background).Color==Color.FromRgb(32,37,48)&&All(dialog).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Close"));dialog.Opacity=0;dialog.Show();dialog.UpdateLayout();CaptureVisual((FrameworkElement)dialog.Content,"stop-result");}finally{dialog.Close();}
        typeof(WhatThePort.App.Panel).GetField("stopDetails",PrivateInstance).SetValue(panel,null);
    }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr owner,int message,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr owner,int message,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr handle);
    static void ProcessDetailsLoadingTests(){
        var server=((WhatThePort.Snapshot)Field("current")).Servers[0];var original=server.Processes;
        try{
            server.Processes=Enumerable.Range(0,500).Select(i=>new WhatThePort.ProcessNode{Pid=100000+i,Name="worker-"+i,Started=server.Started,Memory=WhatThePort.Format.MB,Depth=1}).ToList();
            typeof(WhatThePort.App.Panel).GetField("processesExpanded",PrivateInstance).SetValue(panel,false);Rerender();
            var processes=All(Body()).OfType<Expander>().Single();
            Check("collapsed process details defer row creation",((StackPanel)processes.Content).Children.Count==0);
            var watch=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<10;i++)Rerender();watch.Stop();
            processes=All(Body()).OfType<Expander>().Single();
            Console.WriteLine("PERF collapsed detail: 500 processes; 10 refreshes; "+watch.ElapsedMilliseconds+" ms; child rows="+((StackPanel)processes.Content).Children.Count);
            processes.IsExpanded=true;Layout();
            Check("expanding process details loads every current row",((StackPanel)processes.Content).Children.Count==500&&All(processes).OfType<TextBlock>().Any(t=>t.Text.Contains("worker-499")));
            server.Processes=new List<WhatThePort.ProcessNode>{new WhatThePort.ProcessNode{Pid=199999,Name="replacement-worker",Started=server.Started}};Rerender();
            processes=All(Body()).OfType<Expander>().Single();
            Check("expanded process refresh preserves expansion and current data",processes.IsExpanded&&((StackPanel)processes.Content).Children.Count==1&&All(processes).OfType<TextBlock>().Any(t=>t.Text.Contains("replacement-worker")));
            processes.IsExpanded=false;Rerender();processes=All(Body()).OfType<Expander>().Single();
            Check("collapsed process refresh stops constructing child rows",!processes.IsExpanded&&((StackPanel)processes.Content).Children.Count==0);
        }finally{server.Processes=original;typeof(WhatThePort.App.Panel).GetField("processesExpanded",PrivateInstance).SetValue(panel,false);Rerender();}
    }
    static object Field(string name){return typeof(WhatThePort.App.Panel).GetField(name,PrivateInstance).GetValue(panel);}
    static void Invoke(string name,params object[] args){typeof(WhatThePort.App.Panel).GetMethod(name,PrivateInstance).Invoke(panel,args);}
    static void TrayEvent(IntPtr owner,int code){SendMessage(owner,0x8002,IntPtr.Zero,new IntPtr((1<<16)|code));Pump();}
    static void TrayLifetimeTests(){
        // Replace the demo panel with isolated real preferences; never execute server actions.
        typeof(WhatThePort.App.Panel).GetField("closing",PrivateInstance).SetValue(panel,true);
        ((TrayIcon)Field("tray")).Dispose();((HwndSource)Field("notificationWindow")).Dispose();window.Close();
        string storage=Path.Combine(Path.GetTempPath(),"wtp-ui-"+Guid.NewGuid().ToString("N"));
        try{
            new WhatThePort.Store(storage).SaveSettings(new WhatThePort.Settings{Notifications=false,Cleanup="Off"});
            panel=new WhatThePort.App.Panel(false,false,storage,"en",Path.Combine(storage,"startup"));window=null;
            var tray=(TrayIcon)Field("tray");var owner=(IntPtr)Field("handle");
            Check("background startup creates tray without a WPF panel",panel.Window==null&&tray.Visible&&IsWindow(owner));
            SendMessage(owner,0x8002,IntPtr.Zero,new IntPtr((2<<16)|0x400));
            Check("unrelated tray icon callbacks are ignored",panel.Window==null);
            SendMessage(owner,0x8002,IntPtr.Zero,new IntPtr((1<<16)|0x400));window=panel.Window;window.Opacity=0;Layout();
            Check("native tray click opens the deferred panel",panel.IsVisible&&Title()=="What the Port");
            Check("footer explains the shortcut and refresh interval",((TextBlock)window.FindName("Status")).Text.Contains("Ctrl+Alt+P show/hide")&&((TextBlock)window.FindName("Status")).Text.Contains("every 3s"));
            SendMessage(owner,0x0312,new IntPtr(1),IntPtr.Zero);Pump();Check("hotkey hides the visible panel while tray stays active",!panel.IsVisible&&tray.Visible);
            SendMessage(owner,0x0312,new IntPtr(1),IntPtr.Zero);Layout();
            Click((Button)window.FindName("Pin"));
            typeof(WhatThePort.App.Panel).GetField("current",PrivateInstance).SetValue(panel,Demo.Create());Rerender();
            Click(Rows().First());
            Check("unregistered session and preview share one link management button",!All(window).OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Resume linked agent session (A)"||AutomationProperties.GetName(b)=="Open Vercel preview (V)")&&All(window).OfType<Button>().Count(b=>AutomationProperties.GetName(b)=="Manage project links and copy local URL")==1);
            Invoke("Navigate","list");
            Click(Rows().First());var chart=All(Body()).OfType<Chart>().First();chart.Focus();Press(Key.Home);var selectedTime=chart.SelectedTime;
            string detailTitle=Title();TrayEvent(owner,0x401);
            Check("native tray keyboard activation hides panel and releases chart controls",!panel.IsVisible&&Body().Children.Count==0);
            Invoke("SuspendWindow");Check("idle release closes only the panel and retains the tray",panel.Window==null&&tray.Visible&&IsWindow(owner));
            TrayEvent(owner,0x400);window=panel.Window;window.Opacity=0;Layout();
            Check("reopening restores detail and pin state",Title()==detailTitle&&((Button)window.FindName("Pin")).Content.ToString()=="◆");
            Check("reopening restores chart selection",All(Body()).OfType<Chart>().First().SelectedTime==selectedTime);
            for(int i=0;i<3;i++){
                window.Hide();Invoke("SuspendWindow");SendMessage(owner,0x0312,new IntPtr(1),IntPtr.Zero);window=panel.Window;window.Opacity=0;Layout();
                Check("hotkey reopens released panel "+(i+1),panel.IsVisible&&Title()==detailTitle&&tray.Visible);
            }
            Click((Button)window.FindName("Back"));Click(ByName("Settings (Ctrl+,)"));
            var input=All(Body()).OfType<TextBox>().First();input.Text="17";window.Hide();Invoke("SuspendWindow");
            Check("idle release preserves unsaved settings",panel.Window==window&&input.Text=="17");
            TrayEvent(owner,0x405);Check("notification click reveals unsaved settings",panel.IsVisible&&Title()=="Settings"&&input.Text=="17");
            Click((Button)window.FindName("Back"));Click(Rows().First());Click(ByName("Manage project links and copy local URL"));
            input=All(Body()).OfType<TextBox>().First();input.Text="unsaved-link";window.Hide();Invoke("SuspendWindow");
            Check("idle release preserves unsaved project links",panel.Window==window&&input.Text=="unsaved-link");
            panel.ShowFromLaunch();Layout();Click((Button)window.FindName("Back"));window.Hide();Invoke("SuspendWindow");
            SendMessage(owner,RegisterWindowMessage("TaskbarCreated"),IntPtr.Zero,IntPtr.Zero);
            Check("tray survives Explorer recovery notification while UI is released",tray.Visible&&panel.Window==null);
            // Dismiss only this test-owned popup; never synthesize global mouse/keyboard input.
            using(var dismiss=new System.Threading.Timer(delegate{PostMessage(owner,0x001F,IntPtr.Zero,IntPtr.Zero);},null,250,250)){
                SendMessage(owner,0x8002,new IntPtr(-1),new IntPtr((1<<16)|0x7B));
            }
            Check("native tray context menu opens and dismisses without creating panel",tray.Visible&&panel.Window==null);
            panel.ShowFromLaunch();window=panel.Window;window.Opacity=0;Layout();
            Check("explicit activation still works after release",panel.IsVisible);
            LanguagePreviewTests(storage);
            tray.Dispose();tray.Dispose();Check("tray disposal is idempotent",!tray.Visible);
        }finally{if(Directory.Exists(storage))Directory.Delete(storage,true);}
    }
    static ComboBox LanguageChoice(){return All(Body()).OfType<ComboBox>().Single(c=>(string)c.Tag=="select-LANGUAGE / 언어");}
    static void ChooseLanguage(string choice){var language=LanguageChoice();language.Focus();language.IsDropDownOpen=true;language.SelectedItem=choice;Layout();Pump();}
    static void LanguagePreviewTests(string storage){
        Invoke("Navigate","list");
        Click(ByName("Settings (Ctrl+,)"));
        var startup=All(Body()).OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(t=>(string)t.Tag=="startup");startup.IsChecked=true;
        Click(ByName("Discard changes"));Check("cancel leaves actual isolated startup registration off",!WhatThePort.StartupRegistration.IsEnabled(Path.Combine(storage,"startup")));
        Click(ByName("Settings (Ctrl+,)"));startup=All(Body()).OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(t=>(string)t.Tag=="startup");startup.IsChecked=true;
        All(Body()).OfType<Slider>().Single().Value=70;Click(ByName("Save preferences"));
        Check("save applies actual isolated startup registration and opacity",WhatThePort.StartupRegistration.IsEnabled(Path.Combine(storage,"startup"))&&new WhatThePort.Store(storage).LoadSettings().PanelOpacity==70);
        Click(ByName("Settings (Ctrl+,)"));All(Body()).OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(t=>(string)t.Tag=="startup").IsChecked=false;Click(ByName("Save preferences"));
        Check("startup toggle off removes isolated registration",!WhatThePort.StartupRegistration.IsEnabled(Path.Combine(storage,"startup")));
        string blockedShortcut=Path.Combine(storage,"startup","What the Port.lnk");Directory.CreateDirectory(blockedShortcut);
        Click(ByName("Settings (Ctrl+,)"));All(Body()).OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(t=>(string)t.Tag=="startup").IsChecked=true;All(Body()).OfType<TextBox>().First().Text="17";Click(ByName("Save preferences"));
        Check("failed startup registration rolls back persisted preferences",Title()=="Settings"&&new WhatThePort.Store(storage).LoadSettings().ScanSeconds==3&&!String.IsNullOrEmpty(Message()));
        Directory.Delete(blockedShortcut);Click(ByName("Discard changes"));
        Click(ByName("Settings (Ctrl+,)"));ChooseLanguage("한국어");Click(ByName("설정 저장"));
        string preferences=Path.Combine(storage,"settings.json"),saved=File.ReadAllText(preferences);
        Click(ByName("설정 (Ctrl+,)"));
        All(Body()).OfType<TextBox>().First().Text="unfinished";
        All(Body()).OfType<CheckBox>().First().IsChecked=true;
        All(Body()).OfType<ComboBox>().Single(c=>(string)c.Tag=="select-AUTOMATIC CLEAN UP").SelectedIndex=1;
        Click(ByName("설정 저장"));
        ChooseLanguage("English");
        Check("language preview immediately translates labels, header and actions",Title()=="Settings"&&String.Equals(window.Language.IetfLanguageTag,"en-US",StringComparison.OrdinalIgnoreCase)&&ByName("Discard changes")!=null&&ByName("Save preferences")!=null&&All(Body()).OfType<TextBlock>().Any(t=>t.Text=="A quieter laptop, on your terms."));
        Check("language preview also translates the existing validation notice",Message()=="Enter valid numbers for the sampling and alert thresholds.");
        Check("language preview preserves incomplete input and other unsaved preferences",All(Body()).OfType<TextBox>().First().Text=="unfinished"&&All(Body()).OfType<CheckBox>().First().IsChecked==true&&All(Body()).OfType<ComboBox>().Single(c=>(string)c.Tag=="select-AUTOMATIC CLEAN UP").SelectedItem.ToString()=="Ask");
        Check("preview never saves or changes active monitoring settings",File.ReadAllText(preferences)==saved&&((WhatThePort.Settings)Field("settings")).Language=="ko"&&((WhatThePort.Settings)Field("settings")).Cleanup=="Off");
        CaptureVisual((FrameworkElement)window.Content,"settings-language-preview-en");
        ChooseLanguage("한국어");ChooseLanguage("English");
        Check("repeated language previews preserve draft values",All(Body()).OfType<TextBox>().First().Text=="unfinished");
        window.Hide();Invoke("SuspendWindow");panel.ShowFromLaunch();Layout();
        Check("hiding settings preserves the preview and draft",Title()=="Settings"&&All(Body()).OfType<TextBox>().First().Text=="unfinished");
        Click(ByName("Discard changes"));Check("Cancel restores Korean without saving preview",ByName("설정 (Ctrl+,)")!=null&&String.Equals(window.Language.IetfLanguageTag,"ko-KR",StringComparison.OrdinalIgnoreCase)&&File.ReadAllText(preferences)==saved);
        CaptureVisual((FrameworkElement)window.Content,"footer-shortcut-ko");
        Click(ByName("설정 (Ctrl+,)"));
        Check("cancelled draft is discarded on reopening settings",LanguageChoice().SelectedItem.ToString()=="한국어"&&All(Body()).OfType<TextBox>().First().Text=="3"&&All(Body()).OfType<CheckBox>().First().IsChecked==false);
        ChooseLanguage("English");Click((Button)window.FindName("Back"));Check("Back also cancels language preview",ByName("설정 (Ctrl+,)")!=null);
        Click(ByName("설정 (Ctrl+,)"));ChooseLanguage("English");Press(Key.Escape);Check("Escape also cancels language preview",ByName("설정 (Ctrl+,)")!=null);
        Click(ByName("설정 (Ctrl+,)"));ChooseLanguage("English");Click(ByName("Save preferences"));
        Check("Save persists the previewed language",new WhatThePort.Store(storage).LoadSettings().Language=="en"&&ByName("Settings (Ctrl+,)")!=null);
    }
    static IEnumerable<DependencyObject> Visuals(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Visuals(VisualTreeHelper.GetChild(root,i)))yield return child;}
    static void ToolTipTests(){
        var strings=new Strings("ko");string reason="Terminal, agent, system, database or container process. Stop it in its own app.";
        string heading=(string)((TextBlock)window.FindName("Title")).ToolTip;
        Check("tooltip sentence breaks preserve paths, URLs and decimal points",ToolTipTextConverter.Sentences("Drag to move. Position is saved.")=="Drag to move.\nPosition is saved."&&ToolTipTextConverter.Sentences("위치 변경·저장. 방향키로도 이동 가능.")=="위치 변경·저장.\n방향키로도 이동 가능."&&ToolTipTextConverter.Sentences(@"C:\project. folder\app.js")==@"C:\project. folder\app.js"&&ToolTipTextConverter.Sentences(@"\\server\project. folder\app.js")==@"\\server\project. folder\app.js"&&ToolTipTextConverter.Sentences("https://my-preview.vercel.app")=="https://my-preview.vercel.app"&&ToolTipTextConverter.Sentences("Memory 1.5 GB. CPU 0.1%.")=="Memory 1.5 GB.\nCPU 0.1%.");
        foreach(string text in new[]{strings.T(reason),reason,@"C:\projects\"+new string('x',150)+@"\development-server",heading,strings.T(heading)}){
            // WPF creates string tooltips outside the window tree; do not assign a style in the test.
            var tip=new ToolTip{Content=text,PlacementTarget=window};TextBlock.SetTextAlignment(tip,TextAlignment.Center);
            try{
                tip.IsOpen=true;Pump();tip.UpdateLayout();
                var block=Visuals(tip).OfType<TextBlock>().First();
                Check("long tooltip renders complete left-aligned text on multiple lines within the panel width",block.Text.Replace("\n"," ")==text&&block.TextAlignment==TextAlignment.Left&&block.ActualHeight>18&&block.ActualWidth<=314.5&&tip.ActualWidth<=340.5);
                if(text==strings.T(reason))CaptureVisual(tip,"tooltip-protection-ko");
                if(text==heading)CaptureVisual(tip,"tooltip-title-sentences");
                if(text==strings.T(heading))CaptureVisual(tip,"tooltip-title-sentences-ko");
            }finally{tip.IsOpen=false;Pump();}
        }
        // Open the automatic string tooltip through WPF, then detach its owner as a refresh does.
        var owner=new TextBlock{Text="Temporary tooltip owner",ToolTip=strings.T(reason)};Body().Children.Add(owner);Layout();
        var serviceType=typeof(ToolTip).Assembly.GetType("System.Windows.Controls.PopupControlService");
        var service=serviceType.GetProperty("Current",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null,null);
        try{
            serviceType.GetMethod("ShowToolTip",PrivateInstance).Invoke(service,new object[]{owner,false});Pump();
            var tip=(ToolTip)serviceType.GetProperty("CurrentToolTip",PrivateInstance).GetValue(service,null);
            Body().Children.Remove(owner);Layout();Pump();tip.UpdateLayout();
            Check("automatic tooltip retains dark wrapping style when refresh removes its owner",tip.MaxWidth==340&&Visuals(tip).OfType<TextBlock>().Any(t=>t.TextWrapping==TextWrapping.Wrap));
            CaptureVisual(tip,"tooltip-after-refresh");
        }finally{serviceType.GetMethod("DismissToolTips",PrivateInstance).Invoke(service,null);Body().Children.Remove(owner);Pump();}
    }
    static void CaptureVisual(FrameworkElement visual,string name){
        visual.UpdateLayout();string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","screenshots");Directory.CreateDirectory(folder);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*1.5),(int)Math.Ceiling(visual.ActualHeight*1.5),144,144,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(folder,name+".png")))encoder.Save(file);
    }
    static void LaunchVisibilityTests(){
        // Use a normal demo panel: snapshot panels intentionally suppress auto-hide.
        window.Hide();panel=new WhatThePort.App.Panel(true,false,null,"en");window=panel.Window;window.Opacity=0;
        var deactivate=typeof(WhatThePort.App.Panel).GetMethod("OnDeactivated",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
        panel.ShowFromLaunch();Layout();deactivate.Invoke(panel,new object[]{window,EventArgs.Empty});
        Check("explicit launch remains visible if another window takes focus",window.IsVisible);
        var mouse=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent};window.RaiseEvent(mouse);
        deactivate.Invoke(panel,new object[]{window,EventArgs.Empty});
        Check("normal auto-hide resumes after user interaction",!window.IsVisible);
        panel.ShowFromLaunch();Layout();Click((Button)window.FindName("Hide"));Check("explicit close still hides a newly launched panel",!window.IsVisible);
        panel.ShowFromLaunch();Layout();((Button)window.FindName("Back")).Focus();Press(Key.Escape);Check("Escape still hides a newly launched panel",!window.IsVisible);
        panel.Show();Layout();deactivate.Invoke(panel,new object[]{window,EventArgs.Empty});Check("ordinary tray or hotkey opening retains auto-hide",!window.IsVisible);
    }
    static void HeaderTests(){
        var heading=(TextBlock)window.FindName("Title");string original=heading.Text;
        Check("home heading identifies the app",original=="What the Port"&&window.Title==original);
        foreach(string text in new[]{"What the Port","설정","An exceptionally long project name for the server detail"}){
            heading.Text=text;Layout();var at=heading.TranslatePoint(new Point(),window);
            var back=(Button)window.FindName("Back");var pin=(Button)window.FindName("Pin");
            Check("header stays centered without overlapping controls: "+text,Near(at.X+heading.ActualWidth/2,window.ActualWidth/2)&&heading.TextAlignment==TextAlignment.Center&&at.X>=back.TranslatePoint(new Point(back.ActualWidth,0),window).X&&at.X+heading.ActualWidth<=pin.TranslatePoint(new Point(),window).X);
        }
        heading.Text=original;Layout();
    }
    static void LocalizationTests(){
        var snapshot=WhatThePort.App.Demo.Create();snapshot.Servers[0].Name="Settings";snapshot.Servers[0].Protected=true;snapshot.Servers[0].ProtectionReason="This app or a parent process. Stopping it could close your working session.";
        typeof(WhatThePort.App.Panel).GetField("current",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(panel,snapshot);Rerender();
        var regular=All(Body()).OfType<Grid>().First(g=>(g.Tag as string)=="row-"+snapshot.Servers[0].Key);
        double[] x=regular.Children.OfType<FrameworkElement>().Where(e=>Grid.GetColumn(e)>0).Select(e=>e.TranslatePoint(new Point(),window).X).ToArray();
        Click(ByName("Select servers to stop (C)"));
        var cleanup=All(Body()).OfType<Grid>().First(g=>(g.Tag as string)=="row-"+snapshot.Servers[0].Key);
        double[] y=cleanup.Children.OfType<FrameworkElement>().Where(e=>Grid.GetColumn(e)>0).Select(e=>e.TranslatePoint(new Point(),window).X).ToArray();
        Check("list and cleanup columns retain identical positions",x.Length==y.Length&&x.Zip(y,(a,b)=>Near(a,b)).All(equal=>equal));
        var marker=regular.Children.OfType<FrameworkElement>().Single(e=>Grid.GetColumn(e)==0);var portText=regular.Children.OfType<FrameworkElement>().Single(e=>Grid.GetColumn(e)==1);
        var checkbox=cleanup.Children.OfType<FrameworkElement>().Single(e=>Grid.GetColumn(e)==0);var cleanupPort=cleanup.Children.OfType<FrameworkElement>().Single(e=>Grid.GetColumn(e)==1);
        Check("shield and checkbox centers align with port numbers",Near(marker.TranslatePoint(new Point(0,marker.ActualHeight/2),regular).Y,portText.TranslatePoint(new Point(0,portText.ActualHeight/2),regular).Y)&&Near(checkbox.TranslatePoint(new Point(0,checkbox.ActualHeight/2),cleanup).Y,cleanupPort.TranslatePoint(new Point(0,cleanupPort.ActualHeight/2),cleanup).Y));
        var protectedBox=All(Body()).OfType<CheckBox>().First();
        Check("protected checkbox explains why without enabling stop",!protectedBox.IsEnabled&&ToolTipService.GetShowOnDisabled(protectedBox)&&AutomationProperties.GetHelpText(protectedBox).Contains("parent process"));
        Press(Key.Escape);Click(ByName("Settings (Ctrl+,)"));
        All(Body()).OfType<ComboBox>().Single(c=>(string)c.Tag=="select-LANGUAGE / 언어").SelectedItem="한국어";
        Click(ByName("설정 저장"));
        Check("language save immediately translates header and feedback",Title()=="What the Port"&&Message().Contains("설정"));
        Check("project names are never translated",All(Body()).OfType<TextBlock>().Any(t=>t.Text=="Settings"));
        Check("Korean accessibility names include header and settings",AutomationProperties.GetName((Button)window.FindName("Back"))=="서버 목록"&&ByName("설정 (Ctrl+,)")!=null);
        Click(Rows().First());var chart=All(Body()).OfType<Chart>().First();
        Check("protected detail exposes a persistent explanation card",All(Body()).OfType<Border>().Any(b=>(string)b.Tag=="protection-reason")&&All(Body()).OfType<TextBlock>().Any(t=>t.Text.Contains("작업 세션")&&t.TextWrapping==TextWrapping.Wrap&&t.TextTrimming==TextTrimming.None));
        CaptureVisual((FrameworkElement)window.Content,"protected-detail-ko");
        Check("Korean chart exposes samples and keyboard instructions",UIElementAutomationPeer.CreatePeerForElement(chart).GetName().Contains("측정값")&&UIElementAutomationPeer.CreatePeerForElement(chart).GetHelpText().Contains("방향키"));
        Click(ByName("프로젝트 연결 관리 및 로컬 URL 복사"));Check("Korean project link labels",Title()=="프로젝트 연결"&&All(Body()).OfType<TextBox>().Any(t=>AutomationProperties.GetName(t)=="세션 ID"));
        Press(Key.Escape);Press(Key.Escape);Click(ByName("종료할 서버 선택 (C)"));Check("Korean protected cleanup explanation is visible",All(Body()).OfType<TextBlock>().Any(t=>t.Text.Contains("보호된 항목")));
        Press(Key.Escape);Click(ByName("설정 (Ctrl+,)"));
        var sample=All(Body()).OfType<TextBox>().First();sample.Text="bad";Click(ByName("설정 저장"));Check("invalid numeric input has Korean feedback",Message().Contains("올바른 숫자"));sample.Text="3";
        var modes=All(Body()).OfType<ComboBox>().Single(c=>(string)c.Tag=="select-AUTOMATIC CLEAN UP");Check("cleanup options are localized",modes.Items.Cast<string>().SequenceEqual(new[]{"끄기","알림 후 직접 선택","자동 종료"}));
        All(Body()).OfType<ComboBox>().Single(c=>(string)c.Tag=="select-LANGUAGE / 언어").SelectedItem="English";
        Click(ByName("Save preferences"));Check("English can be restored without restart",Title()=="What the Port"&&ByName("Settings (Ctrl+,)")!=null);
        var strings=new Strings("ko");var outcome=new WhatThePort.StopResult{Port=3000,ListenerStopped=true,Stopped=1};outcome.Errors.Add("Access denied");
        Check("partial stop feedback cannot claim total failure or total success",strings.StopSummary(outcome).Contains("일부 종료")&&strings.StopSummary(outcome).Contains("서버 본체 종료")&&strings.StopSummary(outcome).Contains("오류 1건"));
        foreach(var s in snapshot.Servers)s.Protected=true;Rerender();Click(ByName("Select servers to stop (C)"));
        Check("all-protected list explains zero selectable servers",!ByName("Stop selected server process trees").IsEnabled&&All(Body()).OfType<TextBlock>().Any(t=>t.Text.Contains("There are no servers you can stop")));
        Check("header focus controls stay compact",((Button)window.FindName("Pin")).ActualHeight==32);
    }
    static void NativePlacementTests(){
        PanelPlacement.Place(window,594,null,true,false);Layout();
        var first=PanelPlacement.PhysicalBounds(window);var primary=System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
        Check("first opening uses primary monitor physical work area",first.Left>=primary.Left&&first.Right<=primary.Right+1&&first.Top>=primary.Top&&first.Bottom<=primary.Bottom+1);
        foreach(var screen in System.Windows.Forms.Screen.AllScreens){
            var area=screen.WorkingArea;PanelPlacement.Place(window,716,new Point(area.Left+30,area.Bottom-20),true,false);Layout();Pump();
            var actual=PanelPlacement.PhysicalBounds(window);
            Check("saved placement fits connected monitor "+screen.DeviceName,actual.Left>=area.Left-1&&actual.Right<=area.Right+1&&actual.Top>=area.Top-1&&actual.Bottom<=area.Bottom+1);
        }
        PanelPlacement.Place(window,594,null,true,false);Layout();
        var saved=new Point(first.Left-30,first.Bottom-20);
        PanelPlacement.Place(window,594,saved,true,false);Layout();var moved=PanelPlacement.PhysicalBounds(window);
        window.Hide();window.Show();PanelPlacement.Place(window,594,saved,true,true);Layout();var reopened=PanelPlacement.PhysicalBounds(window);
        Check("saved physical position survives hiding and tray reopening",Near(moved.Left,reopened.Left)&&Near(moved.Bottom,reopened.Bottom));
        PanelPlacement.Place(window,704,saved,false,false);Layout();var detail=PanelPlacement.PhysicalBounds(window);
        Check("native detail placement preserves physical bottom",Near(detail.Bottom,reopened.Bottom));
        PanelPlacement.Place(window,594,new Point(-900000,-900000),true,false);Layout();var work=PanelPlacement.WorkArea(window);
        Check("disconnected monitor position returns to visible work area",window.Left>=work.Left-1&&window.Top>=work.Top-1);
        PanelPlacement.Place(window,594,null,true,false);Layout();
    }
    static void KeyboardTests(){
        window.Activate();Pump();Rows().First().Focus();
        Press(Key.Enter);Check("Enter opens focused server without mouse",Title()=="menubar port monitor");
        var chart=All(Body()).OfType<Chart>().First();chart.Focus();Press(Key.Home);
        Check("chart Home selects first sample",chart.SelectedTime==chart.Samples.First().At);
        Press(Key.Right);Check("chart Right selects next sample",chart.SelectedTime==chart.Samples[1].At);
        var peer=UIElementAutomationPeer.CreatePeerForElement(chart);
        Check("chart screen reader alternative includes value and navigation",peer.GetName().Contains("Sample 2 of")&&peer.GetName().Contains("Memory")&&peer.GetHelpText().Contains("Left and Right"));
        var time=chart.SelectedTime;Rerender();chart=All(Body()).OfType<Chart>().First();
        Check("live refresh preserves chart focus and sample",chart.IsKeyboardFocused&&chart.SelectedTime==time);
        Press(Key.End);Check("chart End selects latest sample",chart.SelectedTime==chart.Samples.Last().At);
        var action=ByName("Open local server (O)");action.Focus();Rerender();
        Check("live refresh preserves footer button focus",ByName("Open local server (O)").IsKeyboardFocused);
        Press(Key.Enter);Check("keyboard activates action with feedback",Message().Contains("simulated"));
        Press(Key.Escape);Check("Escape returns to focused server row",Title()=="What the Port"&&Rows().First().IsKeyboardFocused);
        var pin=(Button)window.FindName("Pin");pin.Focus();
        var visited=new HashSet<DependencyObject>();
        for(int i=0;i<30;i++){var focused=Keyboard.FocusedElement as UIElement;if(focused==null)break;visited.Add(focused);focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));}
        Check("Tab traversal reaches server rows and footer",visited.Contains(Rows().First())&&visited.Contains(ByName("Select servers to stop (C)"))&&visited.Contains(ByName("Settings (Ctrl+,)")));
        Press(Key.C);var check=All(Body()).OfType<CheckBox>().First();check.Focus();bool before=check.IsChecked==true;Press(Key.Space);
        Check("Space toggles cleanup selection",All(Body()).OfType<CheckBox>().First().IsChecked!=before);
        Press(Key.Escape);Rows().First().Focus();
    }
    static void CheckNames(string label){
        var controls=All(window).OfType<Control>().Where(c=>c.IsVisible&&(c is Button||c is CheckBox||c is ComboBox||c is TextBox||c is Expander));
        Check(label,controls.All(c=>{var peer=UIElementAutomationPeer.CreatePeerForElement(c);return peer!=null&&!String.IsNullOrWhiteSpace(peer.GetName());}));
    }
    static void Rerender(){typeof(WhatThePort.App.Panel).GetMethod("Render",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(panel,null);Layout();Pump();}
    static void Pump(){var frame=new System.Windows.Threading.DispatcherFrame();window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>frame.Continue=false));System.Windows.Threading.Dispatcher.PushFrame(frame);}
    static void Press(Key key){
        var target=Keyboard.FocusedElement as UIElement;if(target==null)throw new Exception("No keyboard focus for "+key);
        var source=PresentationSource.FromVisual(window);
        var preview=new KeyEventArgs(Keyboard.PrimaryDevice,source,0,key){RoutedEvent=Keyboard.PreviewKeyDownEvent};target.RaiseEvent(preview);
        if(!preview.Handled){target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,key){RoutedEvent=Keyboard.KeyDownEvent});target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,key){RoutedEvent=Keyboard.KeyUpEvent});}
        Layout();Pump();
    }
    static void PlacementTests(){
        var work=new Rect(0,0,1920,1040);
        var list=PanelPlacement.Calculate(work,new Size(440,594),null);
        var detail=PanelPlacement.Calculate(work,new Size(440,704),list);
        Check("taller view keeps bottom and moves upward",Near(list.Bottom,detail.Bottom)&&Near(list.Top-detail.Top,110));
        var returned=PanelPlacement.Calculate(work,new Size(440,594),detail);
        Check("shorter view returns to original bounds",returned==list);
        var moved=new Rect(200,250,440,594);
        var movedDetail=PanelPlacement.Calculate(work,new Size(440,704),moved);
        Check("navigation respects manually moved panel",Near(movedDetail.Left,moved.Left)&&Near(movedDetail.Bottom,moved.Bottom));
        var shortWork=new Rect(0,0,1280,640);
        var clipped=PanelPlacement.Calculate(shortWork,new Size(440,716),null);
        Check("short display caps height above taskbar",Near(clipped.Height,616)&&Near(clipped.Top,12)&&Near(clipped.Bottom,628));
        var leftMonitor=new Rect(-1920,40,1920,1000);
        var secondary=PanelPlacement.Calculate(leftMonitor,new Size(440,716),null);
        Check("secondary monitor with negative origin and top taskbar",leftMonitor.Contains(secondary)&&Near(secondary.Right,-12)&&Near(secondary.Bottom,1028));
        var upper=new Rect(-900,64,440,460);
        var upperLarge=PanelPlacement.Calculate(leftMonitor,new Size(560,720),upper,12,true);
        var upperSmall=PanelPlacement.Calculate(leftMonitor,new Size(344,460),upperLarge,12,true);
        Check("top anchor respects negative monitor origin and top taskbar",Near(upperLarge.Top,upper.Top)&&Near(upperSmall.Top,upper.Top));
        var topClamped=PanelPlacement.Calculate(shortWork,new Size(560,720),new Rect(40,80,344,460),12,true);
        Check("top anchor clamps to short work area",shortWork.Contains(topClamped)&&Near(topClamped.Top,12)&&Near(topClamped.Bottom,628));
        var sideTaskbar=new Rect(60,0,1220,720);
        var corrected=PanelPlacement.Calculate(sideTaskbar,new Size(440,716),new Rect(-100,200,440,594));
        Check("side taskbar and offscreen bounds are clamped",sideTaskbar.Contains(corrected)&&Near(corrected.Left,72));
        foreach(double scale in new[]{1.0,1.25,1.5,2.0}){
            var dipWork=PanelPlacement.ToDips(new Rect(0,0,1920,1040),new Matrix(1/scale,0,0,1/scale,0,0));
            var fit=PanelPlacement.Calculate(dipWork,new Size(440,716),null);
            Check("physical work area conversion at "+(scale*100)+"% DPI",Near(fit.Bottom*scale,1040-12*scale)&&fit.Top>=12&&dipWork.Contains(fit));
        }
    }
    static bool Near(double a,double b){return Math.Abs(a-b)<1;}
    static void CheckPlacement(string name,double bottom){
        var work=PanelPlacement.WorkArea(window);
        var footer=(FrameworkElement)window.FindName("Footer");
        var status=(FrameworkElement)window.FindName("Status");
        var footerPoint=footer.TransformToAncestor(window).Transform(new Point(0,footer.ActualHeight));
        var statusPoint=status.TransformToAncestor(window).Transform(new Point(0,status.ActualHeight));
        Check(name,Near(window.Top+window.ActualHeight,bottom)&&window.Top>=work.Top&&
            window.Top+window.ActualHeight<=work.Bottom+1&&footer.ActualHeight>0&&
            footerPoint.Y<=window.ActualHeight&&statusPoint.Y<=window.ActualHeight);
    }
    static void Check(string name,bool condition){if(condition){passed++;Console.WriteLine("PASS "+name);}else{failed++;Console.WriteLine("FAIL "+name);}}
    static StackPanel Body(){return (StackPanel)window.FindName("Body");}
    static string Title(){return ((TextBlock)window.FindName("Title")).Text;}
    static string Message(){return ((TextBlock)window.FindName("Message")).Text;}
    static IEnumerable<Button> Rows(){return Body().Children.OfType<Button>().Where(b=>b.Tag is string&&((string)b.Tag).StartsWith("server-"));}
    static Button ByName(string name){return All(window).OfType<Button>().First(b=>AutomationProperties.GetName(b)==name);}
    static IEnumerable<DependencyObject> All(DependencyObject node){yield return node;foreach(var next in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())foreach(var child in All(next))yield return child;}
    static void Click(System.Windows.Controls.Primitives.ButtonBase b){b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Layout();}
    static void Layout(){window.Measure(new Size(window.Width,window.Height));window.Arrange(new Rect(0,0,window.Width,window.Height));window.UpdateLayout();}
    static System.Windows.Controls.Primitives.ScrollBar ScrollBarFrom(ScrollViewer scroll){return scroll.Template.FindName("PART_VerticalScrollBar",scroll) as System.Windows.Controls.Primitives.ScrollBar;}
}
