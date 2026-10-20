using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WhatThePort.App {
    public sealed class Panel {
        public Window Window;
        StackPanel body,footer;
        TextBlock title,status,message;
        Border messageBox;
        Button back,pin;
        readonly Store store;
        readonly string startupFolder;
        readonly Scanner scanner=new Scanner();
        readonly DispatcherTimer timer=new DispatcherTimer();
        readonly DispatcherTimer releaseTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};
        readonly Dictionary<string,DateTime> alerted=new Dictionary<string,DateTime>();
        readonly HashSet<string> selected=new HashSet<string>();
        readonly bool demo,snapshotMode;
        Settings settings;
        sealed class SettingsDraft {
            public string Language,Scan,Memory,Growth,Cpu,Idle,Cleanup,Terminal;
            public bool Notifications,AllListeners,Startup;
            public int Opacity;
        }
        SettingsDraft settingsDraft;
        Strings strings;
        string stopDetails;
        Snapshot current=new Snapshot();
        string view="list",detailKey;
        bool scanning,acting,pinned,closing,processesExpanded,hotkeyRegistered,capturing;
        bool awaitingLaunchInteraction;
        bool releasingWindow;
        string pendingNotice;
        static readonly Size[] PanelSizes={new Size(396,550),new Size(440,594),new Size(500,660),new Size(560,720)};
        double preferredHeight {get{return PanelSizes[settings.PanelSize??1].Height;}}
        int rowIndex;
        DateTime snoozeUntil=DateTime.MinValue,cleanupAsked=DateTime.MinValue;
        TrayIcon tray;
        IntPtr handle;
        HwndSource notificationWindow;
        FontFamily mono;
        static readonly Dictionary<string,Brush> palette=new Dictionary<string,Brush>();
        sealed class ViewBookmark { public string Tag; public DateTime? ChartTime; public double Scroll; }
        ViewBookmark suspendedView;
        public Panel(bool isDemo,bool snapshot,string storageRoot=null,string language=null,string startupDirectory=null) {
            // This small, periodically updated panel does not need a GPU device
            // and its driver allocations. Set the preference before creating the window.
            RenderOptions.ProcessRenderMode=RenderMode.SoftwareOnly;
            demo=isDemo; snapshotMode=snapshot;
            startupFolder=startupDirectory??Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            store=new Store(storageRoot ?? (demo ? Path.Combine(Path.GetTempPath(),"WhatThePort-demo") : null));
            settings=demo?new Settings():store.LoadSettings();
            if(language!=null)settings.Language=language;
            settings.Validate();strings=new Strings(settings.Language);
            timer.Tick+=async delegate { await Refresh(); };
            timer.Interval=TimeSpan.FromSeconds(settings.ScanSeconds);
            releaseTimer.Tick+=delegate{SuspendWindow();};
            if(demo)current=Demo.Create();
            if(!snapshotMode)CreateTray();
            if(demo||snapshotMode){EnsureWindow();Render();}
            if(!String.IsNullOrEmpty(store.LastError))Notice(store.LastError);
        }
        public bool IsVisible {get{return Window!=null&&Window.IsVisible;}}
        void EnsureWindow(){
            if(Window!=null)return;
            var resources=Application.Current.Resources;
            if(!resources.Contains("ToolTipText"))resources["ToolTipText"]=new ToolTipTextConverter();
            Window=(Window)Application.LoadComponent(new Uri("/WhatThePort;component/Shell.xaml",UriKind.Relative));
            Application.Current.MainWindow=Window;
            body=Find<StackPanel>("Body"); footer=Find<StackPanel>("Footer"); title=Find<TextBlock>("Title"); status=Find<TextBlock>("Status"); message=Find<TextBlock>("Message"); messageBox=Find<Border>("MessageBox"); back=Find<Button>("Back"); pin=Find<Button>("Pin");
#if SINGLE_FILE
            var fonts=new Uri("pack://application:,,,/WhatThePort;component/fonts/",UriKind.Absolute);
#else
            var fonts=new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fonts")+Path.DirectorySeparatorChar);
#endif
            Window.FontFamily=new FontFamily(fonts,"./#Geist"); mono=new FontFamily(fonts,"./#Geist Mono");
            // Automatic tooltips outlive list items replaced by Refresh. Keep their
            // resources at application scope so detached popups never fall back to the OS theme.
            resources["ToolTipFont"]=Window.FontFamily;
            resources[typeof(ToolTip)]=Window.Resources[typeof(ToolTip)];
            Window.Resources.Remove(typeof(ToolTip));
            Find<Button>("Hide").Click+=delegate{Window.Hide();}; back.Click+=delegate{Back();};
            Find<Button>("Smaller").Click+=delegate{Try(delegate{ResizePanel(-1);});};
            Find<Button>("Larger").Click+=delegate{Try(delegate{ResizePanel(1);});};
            pin.Click+=delegate{pinned=!pinned; pin.Content=pinned?"◆":"◇"; pin.ToolTip=T(pinned?"Unpin panel":"Keep panel open");AutomationProperties.SetName(pin,(string)pin.ToolTip);};
            pin.Content=pinned?"◆":"◇";
            title.ToolTip=T("Drag to move. Position is saved. Ctrl+Shift+arrow keys also move the panel.");title.Cursor=Cursors.SizeAll;
            Find<Grid>("Header").MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.OriginalSource==title||e.OriginalSource==sender)Window.DragMove();};
            Window.PreviewKeyDown+=OnKey;
            Window.PreviewMouseDown+=delegate{awaitingLaunchInteraction=false;};
            Window.Deactivated+=OnDeactivated;
            Window.IsVisibleChanged+=delegate{if(IsVisible)releaseTimer.Stop();else if(!closing&&!releasingWindow){ReleaseTransientView();if(!demo&&!snapshotMode)releaseTimer.Start();}};
            Window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!closing&&!releasingWindow){e.Cancel=true;Window.Hide();}};
            Window.SourceInitialized+=delegate {
                HwndSource.FromHwnd(new WindowInteropHelper(Window).Handle).AddHook(Hook);
            };
            Notice(pendingNotice);
        }
        void SuspendWindow(){
            // Keep the native notification window alive; release an idle panel after 30s.
            if(acting)return;
            releaseTimer.Stop();
            if(demo||snapshotMode||Window==null||IsVisible||closing||view=="settings"||view=="links")return;
            ReleaseTransientView();releasingWindow=true;
            try {
                Window.Close();Application.Current.MainWindow=null;
                Window=null;body=null;footer=null;title=null;status=null;message=null;messageBox=null;back=null;pin=null;mono=null;
            } finally {releasingWindow=false;}
        }
        T Find<T>(string name) where T:FrameworkElement{return (T)Window.FindName(name);}
        static Brush B(string hex){Brush brush;if(!palette.TryGetValue(hex,out brush)){brush=(Brush)new BrushConverter().ConvertFromString(hex);brush.Freeze();palette.Add(hex,brush);}return brush;}
        string T(string value){return strings.T(value);}
        string F(string template,params object[] values){return strings.F(template,values);}
        TextBlock Text(string value,double size,string color,bool translate=true){return new TextBlock{Text=translate?T(value):value,FontSize=size,Foreground=B(color),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};}
        TextBlock Mono(string value,double size,string color){var t=Text(value,size,color,false);t.FontFamily=mono;return t;}
        Button Button(string label,Action action,string tip){var b=new Button{Content=T(label),ToolTip=T(tip),Tag="action-"+(tip??label)};AutomationProperties.SetName(b,T(tip??label));b.Click+=delegate{Try(action);};return b;}
        void Try(Action action){try{action();}catch(FormatException){Notice("Enter valid numbers for the sampling and alert thresholds.");}catch(OverflowException){Notice("Enter valid numbers for the sampling and alert thresholds.");}catch(Exception e){Notice(e.Message);}}
        void Notice(string value){
            pendingNotice=value;if(Window==null)return;
            message.Text=strings.Error(value);messageBox.Visibility=String.IsNullOrEmpty(value)?Visibility.Collapsed:Visibility.Visible;
            if(!String.IsNullOrEmpty(value)){var peer=UIElementAutomationPeer.CreatePeerForElement(message);if(peer!=null)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);}
        }
        void Rule(){body.Children.Add(new Border{Height=1,Background=B("#30343D"),Margin=new Thickness(-16,13,-16,9)});}
        void Caption(string label){body.Children.Add(new TextBlock{Text=T(label),FontSize=11,Foreground=B("#A4A6AF"),Margin=new Thickness(4,15,0,8)});}
        static void Cell(Grid grid,UIElement child,int column){Grid.SetColumn(child,column);grid.Children.Add(child);}
        Grid Columns(params double[] widths){var g=new Grid();foreach(var width in widths)g.ColumnDefinitions.Add(new ColumnDefinition{Width=width<0?new GridLength(1,GridUnitType.Star):new GridLength(width)});return g;}
        string Protection(Server s){return T(s.ProtectionReason??"This process is protected by the safety policy.");}
        string State(Server s){
            string state=s.Protected?T("Protected process"):!String.IsNullOrEmpty(s.Warning)?strings.Error(s.Warning):(DateTime.UtcNow-s.LastActive).TotalMinutes>=1&&s.Connections==0&&s.Cpu<2?F("Idle {0} · no connections",strings.Duration(DateTime.UtcNow-s.LastActive)):F("up {0}",strings.Duration(DateTime.UtcNow-s.Started));
            return (s.Agent==null?"":F("Via {0}",s.Agent)+" · ")+state;
        }
        string ClientName(ConnectedApp client){return (client.Project??client.Name)+(client.Agent==null||client.Agent==client.Name?"":" · "+client.Agent);}
        public void Show(){Show(false);}
        public void ShowFromLaunch(){Show(false,true);}
        void Show(bool fromTray,bool fromLaunch=false){
            if(snapshotMode)return;
            EnsureWindow();releaseTimer.Stop();awaitingLaunchInteraction=fromLaunch;
            if(view=="list"||view=="detail"||view=="cleanup")Render();
            Place(true,fromTray);Window.Show();Place(true,fromTray);Window.Activate();Window.Focus();
            if(suspendedView!=null){var bookmark=suspendedView;suspendedView=null;RestoreView(bookmark);}
        }
        void OnDeactivated(object sender,EventArgs e){if(!closing&&!releasingWindow&&IsVisible&&!awaitingLaunchInteraction&&!pinned&&!snapshotMode&&!acting&&view!="settings"&&view!="links")Window.Hide();}
        void ReleaseTransientView(){
            // Lists and charts are reconstructed on opening. Keep unsaved forms intact.
            if(demo||snapshotMode||closing||Window==null||(view!="list"&&view!="detail"&&view!="cleanup")||body.Children.Count==0)return;
            var focused=(Keyboard.FocusedElement??FocusManager.GetFocusedElement(Window)) as DependencyObject;
            while(focused!=null&&!((focused as FrameworkElement)!=null&&((FrameworkElement)focused).Tag is string))focused=VisualTreeHelper.GetParent(focused);
            var chart=focused as Chart;
            suspendedView=new ViewBookmark{Tag=focused==null?null:((FrameworkElement)focused).Tag as string,ChartTime=chart==null?null:chart.SelectedTime,Scroll=Find<ScrollViewer>("Scroll").VerticalOffset};
            FocusManager.SetFocusedElement(Window,null);
            body.Children.Clear();footer.Children.Clear();
        }
        void Place(bool opening,bool fromTray=false){
            Point? saved=settings.PanelLeft.HasValue?(Point?)new Point(settings.PanelLeft.Value,settings.PanelBottom.Value):null;
            PanelPlacement.Place(Window,preferredHeight,saved,opening,fromTray,PanelSizes[settings.PanelSize??1].Width);
        }
        void ResizePanel(int change){
            int size=Math.Max(0,Math.Min(PanelSizes.Length-1,(settings.PanelSize??1)+change));
            if(size==(settings.PanelSize??1))return;
            settings.PanelSize=size;Place(false);RememberPosition();UpdateSizeButtons();
        }
        void UpdateSizeButtons(){
            var smaller=Find<Button>("Smaller");var larger=Find<Button>("Larger");
            smaller.IsEnabled=(settings.PanelSize??1)>0;larger.IsEnabled=(settings.PanelSize??1)<PanelSizes.Length-1;
            smaller.ToolTip=T("Smaller panel");larger.ToolTip=T("Larger panel");
            AutomationProperties.SetName(smaller,T("Smaller panel"));AutomationProperties.SetName(larger,T("Larger panel"));
        }
        void ApplyOpacity(int opacity){
            var background=Find<Border>("PanelBackground");var brush=background.Background.Clone();brush.Opacity=opacity/100.0;background.Background=brush;
        }
        void RememberPosition(){
            var bounds=PanelPlacement.PhysicalBounds(Window);settings.PanelLeft=(int)Math.Round(bounds.Left);settings.PanelBottom=(int)Math.Round(bounds.Bottom);
            if(!demo)Try(delegate{store.SaveSettings(settings);});
        }
        public void Start(){
            // Program calls Start before Application.Run. Begin the first await
            // inside the dispatcher so its continuation returns to the UI thread.
            timer.Dispatcher.BeginInvoke(new Action(async delegate{
                if(closing||timer.IsEnabled)return;
                timer.Start();await Refresh();
            }));
        }
        public async Task Refresh(){
            if(demo||scanning||acting)return;scanning=true;
            try {
                if(snoozeUntil!=DateTime.MinValue&&DateTime.UtcNow>=snoozeUntil){snoozeUntil=DateTime.MinValue;UpdateSnoozeControls();UpdateTrayMenu();}
                Settings config=settings.Copy();var next=await Task.Run(()=>{lock(scanner)return scanner.Scan(config);});current=next;
                selected.RemoveWhere(k=>!current.Servers.Any(s=>s.Key==k&&!s.Protected));
                if(IsVisible&&(view=="list"||view=="detail"||view=="cleanup"))Render();else if(!IsVisible)ReleaseTransientView();
                UpdateTray(); CheckAlerts();
                if(settings.Cleanup=="Automatic"&&view!="settings"&&view!="cleanup") {
                    var targets=current.Servers.Where(s=>Policy.CleanupCandidate(s,settings,DateTime.UtcNow)).ToList();
                    if(targets.Count>0)await StopServers(targets,true);
                }
            }catch(Exception e){current.Error=e.Message;Notice("Scan unavailable. "+e.Message);if(status!=null)status.Text=T("SCAN PAUSED · retrying automatically");}
            finally{scanning=false;}
        }
        void Navigate(string target){
            EnsureWindow();
            if(view=="settings"&&target=="settings"&&settingsDraft!=null)return;
            if(view=="settings"&&target!="settings"){
                settingsDraft=null;strings.Language=settings.Language;ApplyOpacity(settings.PanelOpacity??100);UpdateTrayMenu();
            }
            view=target;suspendedView=null;Notice(null);Find<ScrollViewer>("Scroll").ScrollToTop();Render();if(IsVisible){
            if(view=="list"){var row=body.Children.OfType<Button>().FirstOrDefault(b=>(b.Tag as string)=="server-"+detailKey);if(row!=null){row.Focus();return;}}
            if(view=="settings"||view=="links"){var input=Descendants(body).OfType<TextBox>().FirstOrDefault();if(input!=null){input.Focus();return;}}
            back.Focus();
        }}
        void Back(){Navigate(view=="links"&&CurrentServer()!=null?"detail":"list");}
        void Render(){
            if(Window==null)return;
            // Preserve keyboard focus and scroll position through live refreshes.
            var focusedElement=Keyboard.FocusedElement as DependencyObject;
            while(focusedElement!=null&&!((focusedElement as FrameworkElement)!=null&&((FrameworkElement)focusedElement).Tag is string))focusedElement=VisualTreeHelper.GetParent(focusedElement);
            string focused=focusedElement==null?null:((FrameworkElement)focusedElement).Tag as string;
            var focusedChart=focusedElement as Chart;DateTime? chartTime=focusedChart==null?null:focusedChart.SelectedTime;
            double offset=Find<ScrollViewer>("Scroll").VerticalOffset;
            if(suspendedView!=null){focused=suspendedView.Tag;chartTime=suspendedView.ChartTime;offset=suspendedView.Scroll;suspendedView=null;}
            body.Children.Clear();footer.Children.Clear();Find<StackPanel>("Summary").Children.Clear();
            Find<StackPanel>("Summary").Visibility=view=="list"||view=="cleanup"?Visibility.Visible:Visibility.Collapsed;
            back.Content=view=="list"?(object)DotGrid():"‹";back.IsEnabled=true;back.FontSize=25;back.Foreground=B("#A4A6AF");
            AutomationProperties.SetName(back,T(view=="list"?"Servers home":view=="links"?"Back to server":"Back to servers"));back.ToolTip=T(view=="list"?"Servers home":view=="links"?"Back to server":"Back to servers (Esc)");
            UpdateSizeButtons();ApplyOpacity(settingsDraft==null?settings.PanelOpacity??100:settingsDraft.Opacity);
            pin.ToolTip=T(pinned?"Unpin panel":"Keep panel open");AutomationProperties.SetName(pin,(string)pin.ToolTip);Find<Button>("Hide").ToolTip=T("Hide to tray (Esc)");AutomationProperties.SetName(Find<Button>("Hide"),T("Hide to tray"));
            title.ToolTip=T("Drag to move. Position is saved. Ctrl+Shift+arrow keys also move the panel.");Window.Language=XmlLanguage.GetLanguage(strings.Language=="ko"?"ko-KR":"en-US");
            status.Text=demo?T("DEMO · SAMPLE DATA · NO SYSTEM ACTIONS"):F("LOCAL ONLY   ·   Ctrl+Alt+P show/hide   ·   every {0}s",settings.ScanSeconds);
            status.ToolTip=demo?null:T("Ctrl+Alt+P shows or hides this panel. Monitoring continues while hidden.");
            if(view=="detail")Detail();else if(view=="cleanup")List(true);else if(view=="settings")SettingsView();else if(view=="links")LinksView();else List(false);
            if(capturing||!Window.IsVisible){Window.Height=preferredHeight;Window.Width=PanelSizes[settings.PanelSize??1].Width;}
            Window.Title=view=="list"?"What the Port":title.Text+" · What the Port";
            var bookmark=new ViewBookmark{Tag=focused,ChartTime=chartTime,Scroll=offset};
            if(IsVisible)RestoreView(bookmark);else suspendedView=bookmark;
        }
        void RestoreView(ViewBookmark bookmark){
            Window.UpdateLayout();Find<ScrollViewer>("Scroll").ScrollToVerticalOffset(bookmark.Scroll);
            if(bookmark.Tag!=null)foreach(var element in Descendants(Window).OfType<FrameworkElement>())if((element.Tag as string)==bookmark.Tag){if(element is Chart&&bookmark.ChartTime.HasValue)((Chart)element).SelectTime(bookmark.ChartTime.Value);element.Focus();element.BringIntoView();break;}
        }
        static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var node in Descendants(child))yield return node;}}
        void Summary(bool cleanup){
            var body=Find<StackPanel>("Summary");
            var targets=current.Servers.Where(s=>selected.Contains(s.Key)).ToList();
            long value=cleanup?targets.Sum(s=>s.Memory):current.ServerMemory;
            var heading=Columns(-1,175);heading.Margin=new Thickness(3,6,3,12);
            var amount=new StackPanel{Orientation=Orientation.Horizontal};string[] parts=Format.Total(value).Split(' ');
            amount.Children.Add(Mono(parts[0],30,"#F5F5F7"));amount.Children.Add(new TextBlock{Text=" "+parts[1],FontFamily=mono,FontSize=12,Foreground=B("#9396A2"),VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,6)});
            Cell(heading,amount,0);var cpu=Text(cleanup?F("freed by stopping {0}",targets.Count):F("CPU (servers)  {0}",Format.Percent(current.Cpu)),11,"#A4A6AF");cpu.HorizontalAlignment=HorizontalAlignment.Right;Cell(heading,cpu,1);body.Children.Add(heading);
            var bar=new Grid{Height=7,Margin=new Thickness(3,0,3,11),ClipToBounds=true};double total=Math.Max(1,current.TotalMemory);double available=Math.Max(0,current.FreeMemory);double used=Math.Min(total-available,current.ServerMemory);double scale=current.ServerMemory>0?Math.Max(0,used)/current.ServerMemory:1;
            foreach(var s in current.Servers)Segment(bar,s.Memory*scale,Format.PortColor(s.Port));
            Segment(bar,Math.Max(0,total-available-used),"#454852");Segment(bar,available,"#2A2E37");body.Children.Add(bar);
            var legend=Text(F("■ Servers {0}   ▪ Other {1}   ▪ Free {2}",Format.Total(current.ServerMemory),Format.Total(Math.Max(0,current.TotalMemory-current.FreeMemory-current.ServerMemory)),Format.Total(current.FreeMemory)),10,"#A4A6AF");legend.Margin=new Thickness(3,0,0,0);legend.ToolTip=F("Resident working set; shared pages may appear in more than one process. System RAM: {0}",Format.Total(current.TotalMemory));body.Children.Add(legend);
        }
        static void Segment(Grid bar,double value,string color){if(value<=0)return;bar.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(value,GridUnitType.Star)});var segment=new Border{Background=B(color),CornerRadius=new CornerRadius(1),Margin=new Thickness(0,0,1,0)};Cell(bar,segment,bar.ColumnDefinitions.Count-1);}
        void List(bool cleanup){
            title.Text=T(cleanup?"Clean up":"What the Port");Summary(cleanup);Rule();
            if(current.Servers.Count==0){
                var empty=new StackPanel{Margin=new Thickness(20,42,20,45),HorizontalAlignment=HorizontalAlignment.Center};
                var glyph=Mono(":_",38,"#7DDBE0");glyph.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(glyph);
                empty.Children.Add(new TextBlock{Text=T("A little breathing room."),FontSize=18,FontWeight=FontWeights.Medium,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,18,0,10)});
                empty.Children.Add(new TextBlock{Text=T("No development servers found.\nStart a local server and it will appear here."),FontSize=12,Foreground=B("#A4A6AF"),TextAlignment=TextAlignment.Center,LineHeight=20});body.Children.Add(empty);
                var more=Button("Show all user listeners",delegate{settings.AllListeners=true;Task ignored=Refresh();},"Include custom executables and other local apps");more.Margin=new Thickness(15,0,15,12);body.Children.Add(more);
            }
            if(cleanup&&current.Servers.Any(s=>s.Protected)){
                    var protection=Text(current.Servers.All(s=>s.Protected)?"All listed processes are protected. There are no servers you can stop here.":"Protected rows cannot be selected. Open server details from the list for the reason.",11,"#FFCF83");
                    protection.TextWrapping=TextWrapping.Wrap;protection.Margin=new Thickness(4,4,4,10);body.Children.Add(protection);
            }
            for(int i=0;i<current.Servers.Count;i++){Server s=current.Servers[i];int index=i;body.Children.Add(ServerRow(s,cleanup,index));}
            if(cleanup){
                var note=Text("Stopping ends the selected process trees immediately.",10,"#A4A6AF");note.Margin=new Thickness(4,10,0,0);body.Children.Add(note);
                note.TextWrapping=TextWrapping.Wrap;
                var buttons=Columns(80,-1);var cancel=Button("Cancel",delegate{Navigate("list");},"Cancel cleanup");cancel.Margin=new Thickness(0,0,8,0);Cell(buttons,cancel,0);
                var targets=current.Servers.Where(s=>selected.Contains(s.Key)&&!s.Protected).ToList();
                var stop=Button(F(targets.Count==1?"Stop {0} server · free {1}":"Stop {0} servers · free {1}",targets.Count,Format.Bytes(targets.Sum(s=>s.Memory))),async delegate{await StopServers(targets,false);},"Stop selected server process trees");stop.Background=B("#FF453A");stop.IsEnabled=targets.Count>0&&!acting&&current.Error==null;Cell(buttons,stop,1);footer.Children.Add(buttons);
            }else{
                var buttons=Columns(-1,130,36);int count=current.Servers.Count(s=>Policy.CleanupCandidate(s,settings,DateTime.UtcNow));
                var clean=Button(T("Clean up")+(count>0?"  "+count:""),EnterCleanup,"Select servers to stop (C)");clean.HorizontalAlignment=HorizontalAlignment.Left;Cell(buttons,clean,0);
                var live=Text(F("{0} servers · monitoring",current.Servers.Count),10,"#A4A6AF");live.HorizontalAlignment=HorizontalAlignment.Right;live.Margin=new Thickness(0,0,10,0);Cell(buttons,live,1);
                var gear=Button("\uE713",delegate{Navigate("settings");},"Settings (Ctrl+,)");gear.FontFamily=new FontFamily("Segoe MDL2 Assets");gear.Background=Brushes.Transparent;gear.FontSize=16;gear.Padding=new Thickness(0);Cell(buttons,gear,2);footer.Children.Add(buttons);
                if(!String.IsNullOrEmpty(stopDetails)){var results=Button("Stop result details",ShowStopDetails,"Stop result details");results.Margin=new Thickness(0,6,0,0);footer.Children.Add(results);}
            }
        }
        UIElement ServerRow(Server s,bool cleanup,int index){
            var row=Columns(26,65,-1,38,64);row.Height=68;row.Margin=new Thickness(0,0,0,1);row.Tag="row-"+s.Key;
            foreach(double height in new[]{16.0,22,16,14})row.RowDefinitions.Add(new RowDefinition{Height=new GridLength(height)});
            if(cleanup){var check=new CheckBox{Width=24,Height=28,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,IsChecked=selected.Contains(s.Key),IsEnabled=!s.Protected,ToolTip=null};ToolTipService.SetShowOnDisabled(check,true);check.Tag="select-"+s.Key;AutomationProperties.SetName(check,F("Select server {0}",s.Port)+(s.Protected?" · "+Protection(s):""));AutomationProperties.SetHelpText(check,s.Protected?Protection(s):T("Stop selected server process trees"));check.Click+=delegate{if(check.IsChecked==true)selected.Add(s.Key);else selected.Remove(s.Key);Render();};Cell(row,check,0);}
            else if(s.Protected){var shield=Text("◇",14,"#A4A6AF");shield.Margin=new Thickness(4,0,0,0);AutomationProperties.SetName(shield,Protection(s));Cell(row,shield,0);}
            var port=Mono(":"+s.Port,13,Format.PortColor(s.Port));port.FontWeight=FontWeights.Medium;port.Margin=new Thickness(0,0,3,0);Cell(row,port,1);
            var name=Text(s.Name,13,"#F5F5F7",false);name.FontWeight=FontWeights.Medium;name.Margin=new Thickness(0,0,10,0);Cell(row,name,2);
            var meta=Text(State(s),10,s.Warning==null?"#A4A6AF":"#FFB224");meta.Margin=new Thickness(0,0,10,0);AutomationProperties.SetHelpText(meta,s.Protected?Protection(s):State(s));Cell(row,meta,2);
            var chart=new Chart{Samples=s.History,Spark=true,Height=20,VerticalAlignment=VerticalAlignment.Center,Stroke=(Color)ColorConverter.ConvertFromString(s.Warning==null?"#B7BAC4":"#FFB224")};Cell(row,chart,3);
            var memory=Mono(Format.Bytes(s.Memory),11,s.Warning==null?"#DADCE4":"#FFB224");memory.HorizontalAlignment=HorizontalAlignment.Right;memory.Margin=new Thickness(0,0,2,0);Cell(row,memory,4);
            foreach(UIElement element in row.Children){Grid.SetRow(element,1);}Grid.SetRow(meta,2);
            if(s.Clients.Count>0){var clients=Text(F("Connected: {0}",String.Join(", ",s.Clients.Select(ClientName).Distinct())),10,"#7DDBE0",false);Cell(row,clients,2);Grid.SetRow(clients,3);Grid.SetColumnSpan(clients,3);AutomationProperties.SetName(clients,clients.Text);}
            if(cleanup)return new Border{Padding=new Thickness(4,1,4,1),Child=row};
            var button=Button("",delegate{rowIndex=index;detailKey=s.Key;Navigate("detail");},F("{0} · port {1} · {2} · {3}",s.Name,s.Port,Format.Bytes(s.Memory),State(s)));button.Content=row;button.Padding=new Thickness(3,0,3,0);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Background=Brushes.Transparent;button.Tag="server-"+s.Key;AutomationProperties.SetHelpText(button,s.Protected?Protection(s):State(s));
            button.ToolTip=null;button.GotKeyboardFocus+=delegate{rowIndex=index;};
            return button;
        }
        Server CurrentServer(){return current.Servers.FirstOrDefault(s=>s.Key==detailKey);}
        void Detail(){
            Server s=CurrentServer();if(s==null){view="list";List(false);Notice("That server has stopped.");return;}
            title.Text=s.Name;
            var top=Columns(-1,124,34);top.Margin=new Thickness(3,1,3,7);Cell(top,Mono(":"+s.Port,29,Format.PortColor(s.Port)),0);Cell(top,Text(F("Running for {0}",strings.Duration(DateTime.UtcNow-s.Started)),11,"#A4A6AF"),1);
            var stop=Button("■",delegate{ConfirmStop(s);},s.Protected?"Protected server":"Stop server");stop.Background=B("#3B2528");stop.Foreground=B("#FF6961");stop.Padding=new Thickness(0);stop.IsEnabled=!s.Protected&&!acting&&current.Error==null;Cell(top,stop,2);body.Children.Add(top);Rule();
            if(s.Protected){
                ToolTipService.SetShowOnDisabled(stop,true);stop.ToolTip=Protection(s);AutomationProperties.SetHelpText(stop,Protection(s));
                var explanation=new StackPanel();var heading=Text("Protected process",12,"#FFCF83");heading.FontWeight=FontWeights.Medium;explanation.Children.Add(heading);
                explanation.Children.Add(new TextBlock{Text=ToolTipTextConverter.Sentences(Protection(s)),FontSize=12,Foreground=B("#DADCE4"),TextWrapping=TextWrapping.Wrap,LineHeight=18,Margin=new Thickness(0,6,0,0)});
                body.Children.Add(new Border{Tag="protection-reason",Background=B("#2C2C2D"),BorderBrush=B("#5C5140"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(12,10,12,10),Margin=new Thickness(3,0,3,12),Child=explanation});
            }
            var link=demo?Demo.LinkFor(s):store.LinkFor(s);
            Property("Started via",s.Agent??s.Processes.First().Name,null);
            Property("Session",link!=null&&!String.IsNullOrEmpty(link.Session)?link.Agent+" · "+link.Session:T("Not linked"),delegate{Navigate("links");});
            Property("Branch",s.Branch??T("No Git branch"),null);
            Property("Folder",s.Folder??T("Unavailable for this process"),delegate{RunAction("terminal");});
            Property("Ports",F("{0}   ·   {1} connections",String.Join(", ",s.Ports),s.Connections),null);
            Property("Connected apps",s.Clients.Count==0?T("No identified local apps"):String.Join("\n",s.Clients.Select(c=>ClientName(c)+"   #"+c.Pid+"   ·   "+c.Connections)),null);
            var processes=new Expander{Header=F("Processes   ·   {0}   ·   {1}",s.Processes.Count,Format.Bytes(s.Memory)),Foreground=B("#B7BAC4"),FontSize=12,Margin=new Thickness(3,0,3,8),IsExpanded=processesExpanded};
            processes.Tag="processes";AutomationProperties.SetName(processes,(string)processes.Header);
            var nodes=new StackPanel();
            Action loadProcesses=delegate{
                if(nodes.Children.Count>0)return;
                foreach(var p in s.Processes){var item=Text(new string(' ',p.Depth*2)+p.Name+"   #"+p.Pid+"   "+Format.Bytes(p.Memory),11,"#A4A6AF");item.Margin=new Thickness(12,6,0,2);nodes.Children.Add(item);}
            };
            processes.Expanded+=delegate{processesExpanded=true;loadProcesses();};
            processes.Collapsed+=delegate{processesExpanded=false;nodes.Children.Clear();};
            if(processesExpanded)loadProcesses();
            processes.Content=nodes;body.Children.Add(processes);
            Rule();
            MetricTitle("Memory",Format.Bytes(s.Memory),s.Warning==null?"#F5F5F7":"#FFB224");
            body.Children.Add(new Chart{UiLanguage=settings.Language,Tag="chart-memory",Samples=s.History,Threshold=settings.MemoryGB*Format.GB,Height=121,Stroke=(Color)ColorConverter.ConvertFromString(s.Warning==null?"#DADCE4":"#FFB224")});
            MetricTitle("CPU",Format.Percent(s.Cpu),"#F5F5F7");body.Children.Add(new Chart{UiLanguage=settings.Language,Tag="chart-cpu",Samples=s.History,Cpu=true,Height=115,Stroke=(Color)ColorConverter.ConvertFromString(Format.PortColor(s.Port))});
            Rule();
            bool hasSession=link!=null&&!String.IsNullOrEmpty(link.Session),hasPreview=link!=null&&!String.IsNullOrEmpty(link.Preview);
            var actions=Columns(-1,40,hasSession?40:0,hasPreview?40:0);var open=Button(F("Open localhost:{0}",s.Port),delegate{RunAction("open");},"Open local server (O)");open.Background=B("#F5F5F7");open.Foreground=B("#111318");open.Margin=new Thickness(0,0,6,0);Cell(actions,open,0);
            string[] keys={"terminal","session","preview"},labels={">_","A","↗"},tips={"Open project in terminal (T)","Resume linked agent session (A)","Open Vercel preview (V)"};
            for(int i=0;i<3;i++){if(i==1&&!hasSession||i==2&&!hasPreview)continue;string action=keys[i];var b=Button(labels[i],delegate{RunAction(action);},tips[i]);b.Padding=new Thickness(0);b.Margin=new Thickness(2,0,0,0);Cell(actions,b,i+1);}footer.Children.Add(actions);
            var links=Button("Link session & preview   ·   Copy URL",delegate{Navigate("links");},"Manage project links and copy local URL");links.Background=Brushes.Transparent;links.Foreground=B("#A4A6AF");links.FontSize=10;links.MinHeight=25;links.Padding=new Thickness(0,5,0,0);footer.Children.Add(links);
        }
        void MetricTitle(string label,string value,string color){var row=Columns(64,-1,45);row.Margin=new Thickness(3,4,3,2);Cell(row,Text(label,12,"#A4A6AF"),0);Cell(row,Mono(value,12,color),1);Cell(row,Mono(T("10 min"),10,"#A4A6AF"),2);body.Children.Add(row);}
        void Property(string key,string value,Action action){var row=Columns(88,-1);row.Margin=new Thickness(3,4,3,4);Cell(row,Text(key,11,"#A4A6AF"),0);if(action==null){var t=Text(value,12,"#DADCE4",false);t.ToolTip=value;Cell(row,t,1);}else{var b=Button(value,action,value);b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Background=Brushes.Transparent;b.MinHeight=24;b.Padding=new Thickness(0);var t=Text(value,12,"#DADCE4",false);b.Content=t;Cell(row,b,1);}body.Children.Add(row);}
        void EnterCleanup(){selected.Clear();foreach(var s in current.Servers.Where(s=>Policy.CleanupCandidate(s,settings,DateTime.UtcNow)))selected.Add(s.Key);Navigate("cleanup");}
        void ConfirmStop(Server s){
            acting=true;bool confirmed=false;
            try { confirmed=MessageBox.Show(Window,F("Stop :{0} and its {1} process(es)?\n\nThis ends the process tree immediately.",s.Port,s.Processes.Count),T("Stop server"),MessageBoxButton.OKCancel,MessageBoxImage.Warning)==MessageBoxResult.OK; }
            finally{acting=false;}
            if(confirmed){Task ignored=StopServers(new List<Server>{s},false);}
        }
        async Task StopServers(List<Server> targets,bool automatic){
            if(acting)return;
            if(demo){foreach(var s in targets)current.Servers.Remove(s);Navigate("list");Notice("Demo: selected servers removed. No processes were stopped.");return;}
            acting=true;
            try {
                Settings config=settings.Copy();
                var result=await Task.Run(()=>{lock(scanner){
                    // Re-scan immediately before every stop; no cached selection can authorize a new PID.
                    Snapshot fresh=scanner.Scan(config);var outcomes=new List<StopResult>();var notes=new List<string>();
                    foreach(var target in targets){var live=fresh.Servers.FirstOrDefault(s=>s.Key==target.Key);if(live==null){notes.Add(F(":{0} · no longer listening (already stopped or changed).",target.Port));continue;}if(automatic&&!Policy.CleanupCandidate(live,config,DateTime.UtcNow)){notes.Add(F(":{0} · automatic cleanup skipped after recheck.",target.Port));continue;}outcomes.Add(ProcessControl.StopDetailed(live,Native.Stop));}
                    return Tuple.Create(scanner.Scan(config),outcomes,notes);
                }});
                var summaries=result.Item2.Select(r=>strings.StopSummary(r)).Concat(result.Item3).ToList();
                var details=new List<string>();foreach(var outcome in result.Item2){details.Add(strings.StopSummary(outcome));foreach(var error in outcome.Errors){int split=error.IndexOf(": ",StringComparison.Ordinal);details.Add(split<0?strings.Error(error):error.Substring(0,split+2)+strings.Error(error.Substring(split+2)));}if(outcome.Skipped>0)details.Add(F("Protected processes skipped: {0}.",outcome.Skipped));}
                details.AddRange(result.Item3);if(result.Item2.Any(r=>r.ListenerStopped&&r.Errors.Count>0))details.Add(T("A server disappears when it stops listening, even if a child could not be stopped."));
                stopDetails=String.Join("\n",details);
                current=result.Item1;if(!automatic)Navigate("list");else Render();
                string text=String.Join("\n",summaries.Take(2));if(summaries.Count>2||result.Item2.Any(r=>r.Errors.Count>0))text+="\n"+T("Stop result details");
                if(automatic)Notify("Automatic clean up",text);else Notice(text);
                UpdateTray();
            }catch(Exception e){Notice("Could not stop servers. "+e.Message);}finally{acting=false;}
        }
        Window StopDetailsWindow(){
            var dialog=new Window{Owner=Window,Title=T("Stop result details"),Width=Math.Min(Window.ActualWidth,440),Height=300,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,Foreground=B("#F5F5F7"),FontFamily=Window.FontFamily,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false};
            var layout=new DockPanel{Margin=new Thickness(16)};
            var heading=Text("Stop result details",16,"#F5F5F7");heading.Margin=new Thickness(2,2,2,14);DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
            var close=Button("Close",dialog.Close,"Close");close.Style=(Style)Window.FindResource(typeof(Button));close.Margin=new Thickness(0,12,0,0);DockPanel.SetDock(close,Dock.Bottom);layout.Children.Add(close);
            var text=new TextBox{Text=stopDetails,Style=(Style)Window.FindResource(typeof(TextBox)),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Background=B("#202530"),Foreground=B("#F5F5F7"),BorderThickness=new Thickness(0),Padding=new Thickness(10),FontSize=12};AutomationProperties.SetName(text,T("Stop result details"));layout.Children.Add(text);
            dialog.Content=new Border{CornerRadius=new CornerRadius(16),BorderBrush=B("#53555E"),BorderThickness=new Thickness(1),Background=B("#1C2029"),Child=layout};
            heading.MouseLeftButtonDown+=delegate{dialog.DragMove();};
            dialog.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape)dialog.Close();};
            return dialog;
        }
        void ShowStopDetails(){
            var dialog=StopDetailsWindow();
            acting=true;try{dialog.ShowDialog();}finally{acting=false;}
        }
        void RunAction(string action){
            var s=CurrentServer();if(s==null)return;
            if(demo){Notice(F("Demo: {0} action simulated.",action));return;}
            var link=store.LinkFor(s);
            if(action=="open")Launchers.OpenLocal(s);
            else if(action=="terminal")Launchers.Terminal(s.Folder,settings.Terminal,null);
            else if(action=="session"){if(link==null||String.IsNullOrEmpty(link.Session))Notice("Register this server's exact session ID to resume it.");else Launchers.Terminal(s.Folder,settings.Terminal,link);}
            else if(action=="preview"){if(link==null||String.IsNullOrEmpty(link.Preview))Notice("Register the Vercel preview URL for this project.");else Launchers.OpenPreview(link);}
        }
        TextBox Input(string label,string value,StackPanel target=null){
            var host=target??body;
            host.Children.Add(new TextBlock{Text=T(label),FontSize=11,Foreground=B("#A4A6AF"),Margin=new Thickness(4,15,3,8),TextWrapping=TextWrapping.Wrap,MinHeight=target==null?0:32});
            var input=new TextBox{Tag="input-"+label,Text=value??"",Height=34,VerticalContentAlignment=VerticalAlignment.Center,Padding=new Thickness(9,2,9,2),Margin=new Thickness(3,0,3,0)};AutomationProperties.SetName(input,T(label));host.Children.Add(input);return input;
        }
        ComboBox Select(string label,string value,params string[] choices){Caption(label);var combo=new ComboBox{Tag="select-"+label,ItemsSource=choices,SelectedItem=value,Margin=new Thickness(3,0,3,0)};AutomationProperties.SetName(combo,T(label));body.Children.Add(combo);return combo;}
        CheckBox Check(string label,bool value){var check=new CheckBox{Tag="check-"+label,Content=T(label),IsChecked=value,Margin=new Thickness(3,15,0,0)};AutomationProperties.SetName(check,T(label));body.Children.Add(check);return check;}
        void SettingsView(){
            if(settingsDraft==null)settingsDraft=new SettingsDraft{Language=settings.Language,Scan=settings.ScanSeconds.ToString(),Memory=settings.MemoryGB.ToString(CultureInfo.InvariantCulture),Growth=settings.GrowthMB.ToString(),Cpu=settings.CpuPercent.ToString(),Idle=settings.IdleHours.ToString(CultureInfo.InvariantCulture),Notifications=settings.Notifications,Cleanup=settings.Cleanup,Terminal=settings.Terminal,AllListeners=settings.AllListeners,Startup=!demo&&StartupRegistration.IsEnabled(startupFolder),Opacity=settings.PanelOpacity??100};
            var draft=settingsDraft;
            title.Text=T("Settings");
            var intro=Text("A quieter laptop, on your terms.",16,"#F5F5F7");intro.Margin=new Thickness(3,8,0,3);body.Children.Add(intro);
            var language=Select("LANGUAGE / 언어",draft.Language=="ko"?"한국어":"English","한국어","English");
            var fields=new UniformGrid{Columns=2};var cells=new StackPanel[4];for(int i=0;i<4;i++){cells[i]=new StackPanel();fields.Children.Add(cells[i]);}body.Children.Add(fields);
            var scan=Input("SAMPLE INTERVAL · seconds (1–30)",draft.Scan,cells[0]);
            var memory=Input("MEMORY ALERT · GB",draft.Memory,cells[1]);
            var growth=Input("GROWTH ALERT · MB / 10 min (0 = off)",draft.Growth,cells[2]);
            var cpu=Input("CPU ALERT · % of this computer",draft.Cpu,cells[3]);
            var notify=Check("Desktop notifications",draft.Notifications);
            string[] modes={"Off","Ask","Automatic"};var cleanup=Select("AUTOMATIC CLEAN UP",T(draft.Cleanup),modes.Select(T).ToArray());
            var idle=Input("IDLE THRESHOLD · hours (0.05–720)",draft.Idle);
            var explanation=new TextBlock{Text=T("Only continuously observed idle servers are eligible. Protected and alerting servers are never stopped automatically."),FontSize=11,Foreground=B("#A4A6AF"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,9,3,0)};body.Children.Add(explanation);
            var terminal=Select("PREFERRED TERMINAL",draft.Terminal,"PowerShell","Windows Terminal");
            var all=Check("Show all user TCP listeners",draft.AllListeners);
            var startupRow=Columns(-1,46);startupRow.Margin=new Thickness(3,18,3,0);Cell(startupRow,Text("Start when I sign in to Windows",12,"#F5F5F7"),0);
            var startup=new ToggleButton{Tag="startup",Style=(Style)Window.FindResource("Switch"),IsChecked=draft.Startup};AutomationProperties.SetName(startup,T("Start when I sign in to Windows"));startup.ToolTip=T("Starts this EXE in the tray. Save again after moving the file.");Cell(startupRow,startup,1);body.Children.Add(startupRow);
            startup.Checked+=delegate{draft.Startup=true;};startup.Unchecked+=delegate{draft.Startup=false;};
            Caption("BACKGROUND OPACITY");var opacityCaption=(TextBlock)body.Children[body.Children.Count-1];opacityCaption.Text+=" · "+draft.Opacity+"%";
            var opacity= new Slider{Minimum=40,Maximum=100,Value=draft.Opacity,TickFrequency=5,IsSnapToTickEnabled=true,Margin=new Thickness(3,0,3,0),Tag="opacity"};AutomationProperties.SetName(opacity,T("BACKGROUND OPACITY"));body.Children.Add(opacity);
            opacity.ToolTip=draft.Opacity+"%";opacity.ValueChanged+=delegate{draft.Opacity=(int)opacity.Value;opacityCaption.Text=T("BACKGROUND OPACITY")+" · "+draft.Opacity+"%";opacity.ToolTip=draft.Opacity+"%";ApplyOpacity(draft.Opacity);};
            var snooze=Button(T(DateTime.UtcNow<snoozeUntil?"Resume alerts":"Snooze alerts for 1 hour"),ToggleSnooze,"Pause or resume resource notifications");snooze.Margin=new Thickness(3,18,3,4);body.Children.Add(snooze);
            var snoozeState=Text(DateTime.UtcNow<snoozeUntil?F("Alerts paused until {0}",snoozeUntil.ToLocalTime().ToString("HH:mm")):T("Alerts active"),11,"#A4A6AF",false);snoozeState.Tag="snooze-state";snoozeState.Margin=new Thickness(3,3,3,0);body.Children.Add(snoozeState);
            var position=Button("Reset panel position",delegate{settings.PanelLeft=null;settings.PanelBottom=null;if(!demo)store.SaveSettings(settings);Place(true);Notice("Position reset to the primary monitor. Drag the title to save a new position.");},"Reset panel position to primary monitor");position.Margin=new Thickness(3,8,3,4);body.Children.Add(position);
            var positionHelp=new TextBlock{Text=T("Drag the title to move the panel. Position is saved automatically. Keyboard: Ctrl+Shift+arrow keys."),TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=B("#A4A6AF"),Margin=new Thickness(3,4,3,0)};body.Children.Add(positionHelp);
            var help=new TextBlock{Text="Windows 10 / 11 · x64\nWhat the Port for Windows "+AppVersion.Current+"\nBased on WhatThePort by Tomjohn Design · MIT",FontSize=10,Foreground=B("#A4A6AF"),Margin=new Thickness(3,18,3,8),LineHeight=18};body.Children.Add(help);
            var buttons=Columns(90,-1);var cancel=Button("Cancel",delegate{Navigate("list");},"Discard changes");cancel.Margin=new Thickness(0,0,8,0);Cell(buttons,cancel,0);
            var save=Button("Save preferences",delegate{
                var next=new Settings{Language=(string)language.SelectedItem=="English"?"en":"ko",ScanSeconds=Int32.Parse(scan.Text,CultureInfo.InvariantCulture),MemoryGB=Double.Parse(memory.Text,CultureInfo.InvariantCulture),GrowthMB=Int32.Parse(growth.Text,CultureInfo.InvariantCulture),CpuPercent=Int32.Parse(cpu.Text,CultureInfo.InvariantCulture),IdleHours=Double.Parse(idle.Text,CultureInfo.InvariantCulture),Notifications=notify.IsChecked==true,Cleanup=modes[cleanup.SelectedIndex],Terminal=(string)terminal.SelectedItem,AllListeners=all.IsChecked==true};
                next.PanelLeft=settings.PanelLeft;next.PanelBottom=settings.PanelBottom;next.PanelSize=settings.PanelSize;next.PanelOpacity=draft.Opacity;next.Validate();
                if(!demo){
                    store.SaveSettings(next);
                    try{StartupRegistration.Set(draft.Startup,System.Reflection.Assembly.GetEntryAssembly().Location,startupFolder);}
                    catch{store.SaveSettings(settings);throw;}
                }
                settings=next;strings.Language=settings.Language;UpdateTrayMenu();timer.Interval=TimeSpan.FromSeconds(settings.ScanSeconds);Navigate("list");Notice(demo?"Demo preferences applied for this session.":"Preferences saved.");
            },"Save preferences");save.Background=B("#F5F5F7");save.Foreground=B("#111318");Cell(buttons,save,1);footer.Children.Add(buttons);
            language.SelectionChanged+=delegate{
                string chosen=(string)language.SelectedItem=="English"?"en":"ko";if(chosen==draft.Language)return;
                // Keep raw text, including incomplete numeric input, outside the rebuilt controls.
                draft.Language=chosen;draft.Scan=scan.Text;draft.Memory=memory.Text;draft.Growth=growth.Text;draft.Cpu=cpu.Text;draft.Idle=idle.Text;
                draft.Notifications=notify.IsChecked==true;draft.AllListeners=all.IsChecked==true;draft.Cleanup=modes[cleanup.SelectedIndex];draft.Terminal=(string)terminal.SelectedItem;
                language.IsDropDownOpen=false;strings.Language=chosen;Render();Notice(pendingNotice);UpdateTrayMenu();
            };
        }
        void ToggleSnooze(){
            bool paused=DateTime.UtcNow<snoozeUntil;snoozeUntil=paused?DateTime.MinValue:DateTime.UtcNow.AddHours(1);
            UpdateSnoozeControls();
            UpdateTrayMenu();Notice(paused?"Alerts resumed.":"Alerts snoozed for one hour.");
        }
        void UpdateSnoozeControls(){
            if(body==null)return;bool paused=DateTime.UtcNow<snoozeUntil;
            var snooze=Descendants(body).OfType<Button>().FirstOrDefault(b=>(string)b.Tag=="action-Pause or resume resource notifications");if(snooze!=null)snooze.Content=T(paused?"Resume alerts":"Snooze alerts for 1 hour");
            var state=Descendants(body).OfType<TextBlock>().FirstOrDefault(t=>(string)t.Tag=="snooze-state");if(state!=null)state.Text=paused?F("Alerts paused until {0}",snoozeUntil.ToLocalTime().ToString("HH:mm")):T("Alerts active");
        }
        void LinksView(){
            var s=CurrentServer();if(s==null){Navigate("list");return;}title.Text=T("Project links");var link=demo?Demo.LinkFor(s):store.LinkFor(s);
            var intro=new TextBlock{Text=T("Pick up where you left off."),FontSize=18,Margin=new Thickness(3,10,0,0)};body.Children.Add(intro);
            var sub=new TextBlock{Text=F("Link the exact agent session and preview for :{0}. Your conversation history is never read.",s.Port),Foreground=B("#A4A6AF"),TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(3,9,3,0),LineHeight=19};body.Children.Add(sub);
            var folder=Input("PROJECT FOLDER",s.Folder);var agent=Select("AGENT",link==null?"Codex":link.Agent??"Codex","Codex","Claude Code");
            var session=Input("SESSION ID",link==null?"":link.Session);var preview=Input("VERCEL PREVIEW · https://…vercel.app",link==null?"":link.Preview);
            var note=new TextBlock{Text=T("Resume runs in your preferred terminal. Codex or Claude Code must already be installed and signed in."),Foreground=B("#A4A6AF"),TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(3,16,3,0),LineHeight=18};body.Children.Add(note);
            var copy=Button(F("Copy {0}",s.Url),delegate{if(demo)Notice("Demo: URL copy simulated.");else{Clipboard.SetText(s.Url);Notice("Local URL copied.");}},"Copy local server URL");copy.Margin=new Thickness(3,22,3,0);body.Children.Add(copy);
            var buttons=Columns(90,-1);var cancel=Button("Cancel",delegate{Navigate("detail");},"Back to server");cancel.Margin=new Thickness(0,0,8,0);Cell(buttons,cancel,0);
            var save=Button("Save links",delegate{if(demo){Navigate("detail");Notice("Demo: project links simulated.");return;}var item=new ProjectLink{Folder=folder.Text.Trim(),Port=s.Port,Agent=(string)agent.SelectedItem,Session=session.Text.Trim(),Preview=preview.Text.Trim()};item.Validate();if(!String.IsNullOrEmpty(s.Folder)&&!String.Equals(s.Folder.TrimEnd('\\'),item.Folder.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("The folder must match this server's detected project folder.");store.SaveLink(item);Navigate("detail");Notice("Project links saved.");},"Save project links");save.Background=B("#F5F5F7");save.Foreground=B("#111318");Cell(buttons,save,1);footer.Children.Add(buttons);
        }
        void OnKey(object sender,KeyEventArgs e){
            awaitingLaunchInteraction=false;
            if(Keyboard.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift)&&(e.Key==Key.Left||e.Key==Key.Right||e.Key==Key.Up||e.Key==Key.Down)){
                Window.Left+=(e.Key==Key.Left?-20:e.Key==Key.Right?20:0);Window.Top+=(e.Key==Key.Up?-20:e.Key==Key.Down?20:0);Place(false);RememberPosition();e.Handled=true;return;
            }
            var combo=Keyboard.FocusedElement as ComboBox;if(e.Key==Key.Escape&&combo!=null&&combo.IsDropDownOpen)return;
            if(e.Key==Key.Escape){if(view=="list")Window.Hide();else Back();e.Handled=true;return;}
            if(e.Key==Key.OemComma&&Keyboard.Modifiers==ModifierKeys.Control){Navigate("settings");e.Handled=true;return;}
            if(Keyboard.FocusedElement is TextBox||Keyboard.FocusedElement is ComboBox||Keyboard.Modifiers!=ModifierKeys.None)return;
            if(view=="list"&&(e.Key==Key.Down||e.Key==Key.Up)){rowIndex=Math.Max(0,Math.Min(current.Servers.Count-1,rowIndex+(e.Key==Key.Down?1:-1)));if(current.Servers.Count>0){string tag="server-"+current.Servers[rowIndex].Key;foreach(var b in body.Children.OfType<Button>())if((b.Tag as string)==tag)b.Focus();}e.Handled=true;}
            else if(view=="list"&&e.Key==Key.C){EnterCleanup();e.Handled=true;}
            else if(view=="detail"){string action=e.Key==Key.O?"open":e.Key==Key.T?"terminal":e.Key==Key.A?"session":e.Key==Key.V?"preview":null;if(action!=null){Try(delegate{RunAction(action);});e.Handled=true;}}
        }
        IntPtr Hook(IntPtr hwnd,int msg,IntPtr wparam,IntPtr lparam,ref bool handled){
            if(msg==0x0312||msg==0x8001){if(IsVisible)Window.Hide();else Show();handled=true;}
            // Re-evaluate after Windows has applied taskbar, display or DPI changes.
            if(!snapshotMode&&(msg==0x001A||msg==0x007E||msg==0x02E0||msg==0x0232))
                Application.Current.Dispatcher.BeginInvoke(new Action(delegate{if(IsVisible&&!closing){Place(false);if(msg==0x0232)RememberPosition();}}));
            return IntPtr.Zero;
        }
        void CreateTray(){
            notificationWindow=new HwndSource(new HwndSourceParameters("What the Port notifications"){Width=0,Height=0,WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x80});
            handle=notificationWindow.Handle;notificationWindow.AddHook(Hook);
            if(!demo){hotkeyRegistered=Native.RegisterHotKey(handle,1,0x4003,0x50);if(!hotkeyRegistered)Notice("Ctrl+Alt+P is already in use. Open the panel from its tray icon.");}
            tray=new TrayIcon(notificationWindow);
            tray.Click+=delegate{if(IsVisible)Window.Hide();else Show(true);};
            UpdateTrayMenu();
            tray.BalloonClick+=delegate{Show();};
        }
        void UpdateTrayMenu(){
            if(tray==null)return;
            tray.SetMenu(new[]{T("Open What the Port"),T("Clean up…"),T("Settings…"),T(DateTime.UtcNow<snoozeUntil?"Resume alerts":"Snooze alerts for 1 hour"),null,T("Quit")},delegate(int item){if(item==0)Show();else if(item==1){EnterCleanup();Show();}else if(item==2){Navigate("settings");Show();}else if(item==3){ToggleSnooze();if(IsVisible)return;if(settings.Notifications)tray.Notify(T("What the Port"),T(DateTime.UtcNow<snoozeUntil?"Alerts snoozed for one hour.":"Alerts resumed."));}else if(item==5)Quit();});UpdateTray();
        }
        static UIElement DotGrid(){var canvas=new Canvas{Width=19,Height=19};for(int y=0;y<4;y++)for(int x=0;x<4;x++){var dot=new System.Windows.Shapes.Ellipse{Width=3,Height=3,Fill=B("#FFB224")};Canvas.SetLeft(dot,x*5);Canvas.SetTop(dot,y*5);canvas.Children.Add(dot);}return canvas;}
        void UpdateTray(){if(tray==null)return;tray.Update("What the Port · "+F("{0} servers · monitoring",current.Servers.Count)+" · "+Format.Total(current.ServerMemory),current.Servers.Any(s=>s.Warning!=null));}
        void Notify(string heading,string text){if(tray!=null&&settings.Notifications&&DateTime.UtcNow>=snoozeUntil)tray.Notify(T(heading),strings.Error(text));}
        void CheckAlerts(){
            DateTime now=DateTime.UtcNow;
            foreach(var s in current.Servers.Where(s=>s.Warning!=null)){DateTime last;if(!alerted.TryGetValue(s.Key,out last)||(now-last).TotalMinutes>=15){Notify(":"+s.Port+" · "+s.Name,s.Warning);alerted[s.Key]=now;}}
            foreach(var key in alerted.Keys.Where(k=>!current.Servers.Any(s=>s.Key==k)).ToList())alerted.Remove(key);
            int count=current.Servers.Count(s=>Policy.CleanupCandidate(s,settings,now));if(settings.Cleanup=="Ask"&&count>0&&(now-cleanupAsked).TotalHours>=1){Notify("A little breathing room?",F("{0} idle server(s) are ready to clean up. Open Clean up to review.",count));cleanupAsked=now;}
        }
        public void Quit(){closing=true;timer.Stop();releaseTimer.Stop();if(handle!=IntPtr.Zero&&!demo)Native.UnregisterHotKey(handle,1);if(tray!=null)tray.Dispose();if(Window!=null)Window.Close();if(notificationWindow!=null)notificationWindow.Dispose();Application.Current.Shutdown();}
        public async Task<bool> LiveSmoke(string report){
            settings=new Settings{Notifications=false,Cleanup="Off"};
            EnsureWindow();
            IntPtr panelHandle=new WindowInteropHelper(Window).EnsureHandle();
            await Refresh();await Task.Delay(300);await Refresh();
            bool ok=current.Error==null&&current.TotalMemory>0&&tray!=null&&tray.Visible&&handle!=IntPtr.Zero&&panelHandle!=IntPtr.Zero;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report)));
            File.WriteAllText(report,"result="+(ok?"PASS":"FAIL")+"\ntray="+(tray!=null&&tray.Visible)+"\nhotkey="+hotkeyRegistered+"\nwindowHandle="+(panelHandle!=IntPtr.Zero)+"\nservers="+current.Servers.Count+"\ntotalMemory="+current.TotalMemory+"\nerror="+(current.Error??"none")+"\n");
            Quit();return ok;
        }
        public void CaptureAll(string directory){
            Directory.CreateDirectory(directory);capturing=true;pinned=true;Window.Left=30;Window.Top=30;Window.Show();
            foreach(string page in new[]{"list","detail","cleanup","settings","settings-startup","links","empty"}){
                current=Demo.Create();detailKey=current.Servers[0].Key;selected.Clear();if(page=="cleanup")selected.Add(current.Servers[4].Key);if(page=="empty")current.Servers.Clear();Navigate(page=="empty"?"list":page=="settings-startup"?"settings":page);
                Window.UpdateLayout();Find<ScrollViewer>("Scroll").ScrollToTop();Window.UpdateLayout();
                if(page=="settings-startup"){Find<ScrollViewer>("Scroll").ScrollToBottom();Window.UpdateLayout();}
                Window.Dispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(delegate{}));
                var bitmap=new RenderTargetBitmap((int)(Window.ActualWidth*1.5),(int)(Window.ActualHeight*1.5),144,144,PixelFormats.Pbgra32);bitmap.Render((Visual)Window.Content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(directory,page+".png")))encoder.Save(file);
            }
            Quit();
        }
    }
}
