using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WhatThePort.App {
    public sealed class Chart : FrameworkElement {
        const double PlotLeft=54;
        public List<Sample> Samples = new List<Sample>();
        public bool Cpu;
        public string UiLanguage="en";
        bool spark;
        public bool Spark { get{return spark;} set{spark=value;Focusable=!value;KeyboardNavigation.SetIsTabStop(this,!value);} }
        public double Threshold;
        public Color Stroke = Color.FromRgb(207,211,225);
        double? hover;
        int selected=-1;
        List<Sample> previousSamples;
        static readonly DependencyProperty ProgressProperty=DependencyProperty.Register("Progress",typeof(double),typeof(Chart),new FrameworkPropertyMetadata(1.0,FrameworkPropertyMetadataOptions.AffectsRender));
        public DateTime? SelectedTime { get{return selected>=0&&selected<Samples.Count?(DateTime?)Samples[selected].At:null;} }
        public Chart() {
            Height=112;ClipToBounds=true;Focusable=true;AutomationProperties.SetLiveSetting(this,AutomationLiveSetting.Polite);
            MouseMove+=OnMove;MouseLeave+=delegate{hover=null;ToolTip=null;InvalidateVisual();};
            GotKeyboardFocus+=delegate{if(selected<0)selected=Samples.Count-1;BringIntoView();InvalidateVisual();};
            LostKeyboardFocus+=delegate{InvalidateVisual();};
            Unloaded+=delegate{StopTransition();};
        }
        internal void StopTransition(){BeginAnimation(ProgressProperty,null);previousSamples=null;}
        internal void TransitionFrom(Chart previous){
            if(!SystemParameters.ClientAreaAnimation||previous.Samples.Count==0||Samples.Count==0||previous.Samples.Last().At==Samples.Last().At)return;
            previousSamples=previous.Samples;
            var animation=new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(260)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
            animation.Completed+=delegate{previousSamples=null;};BeginAnimation(ProgressProperty,animation);
        }
        void Range(List<Sample> samples,out double min,out double max){
            min=0;max=Cpu?100:32*Format.MB;if(samples.Count==0)return;
            double low=samples.Min(s=>Cpu?Math.Min(100,Math.Max(0,s.Cpu)):Math.Max(0,s.Memory));
            double high=samples.Max(s=>Cpu?Math.Min(100,Math.Max(0,s.Cpu)):Math.Max(0,s.Memory));
            double padding=Math.Max(high-low,Cpu?.1:Math.Max(Format.MB,high*.01))*.15;
            min=Math.Max(0,low-padding);max=high+padding;if(Cpu)max=Math.Min(100,max);
        }
        public void SelectTime(DateTime time){if(Samples.Count>0)selected=Samples.IndexOf(Samples.OrderBy(s=>Math.Abs((s.At-time).TotalSeconds)).First());}
        string SampleText(Sample sample){return sample.At.ToLocalTime().ToString("HH:mm:ss")+" · "+(Cpu?Format.Percent(sample.Cpu):Format.Bytes(sample.Memory));}
        public string AccessibleDescription(){
            var strings=new Strings(UiLanguage);string label=strings.T(Cpu?"CPU":"Memory");
            if(Samples.Count==0)return strings.F("{0} chart. Collecting samples.",label);
            int index=selected<0?Samples.Count-1:Math.Min(selected,Samples.Count-1);
            string range=Cpu?Format.Percent(Samples.Min(s=>s.Cpu))+" to "+Format.Percent(Samples.Max(s=>s.Cpu)):Format.Bytes(Samples.Min(s=>s.Memory))+" to "+Format.Bytes(Samples.Max(s=>s.Memory));
            return strings.F("{0} chart, last 10 minutes. {1} samples. Range {2}. Sample {3} of {1}: {4}.",label,Samples.Count,range.Replace(" to ",strings.T(" to ")),index+1,SampleText(Samples[index]));
        }
        protected override AutomationPeer OnCreateAutomationPeer(){return Spark?null:new ChartPeer(this);}
        sealed class ChartPeer : FrameworkElementAutomationPeer {
            readonly Chart chart;
            public ChartPeer(Chart owner):base(owner){chart=owner;}
            protected override string GetClassNameCore(){return "ResourceChart";}
            protected override AutomationControlType GetAutomationControlTypeCore(){return AutomationControlType.Custom;}
            protected override string GetNameCore(){return chart.AccessibleDescription();}
            protected override string GetHelpTextCore(){return new Strings(chart.UiLanguage).T("Left and Right: previous or next sample. Home and End: first or latest sample. Tab: next control.");}
        }
        protected override void OnKeyDown(KeyEventArgs e){
            base.OnKeyDown(e);if(Spark||Samples.Count==0)return;
            int index=selected<0?Samples.Count-1:selected;
            if(e.Key==Key.Left)index--;else if(e.Key==Key.Right)index++;else if(e.Key==Key.Home)index=0;else if(e.Key==Key.End)index=Samples.Count-1;else return;
            selected=Math.Max(0,Math.Min(Samples.Count-1,index));e.Handled=true;InvalidateVisual();
            var peer=UIElementAutomationPeer.CreatePeerForElement(this);if(peer!=null)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        void OnMove(object sender,MouseEventArgs e) {
            if (Spark || Samples.Count == 0) return;
            hover=e.GetPosition(this).X;
            double ratio=Math.Max(0,Math.Min(1,(hover.Value-PlotLeft)/Math.Max(1,ActualWidth-PlotLeft-4)));
            DateTime time=Samples.Last().At.AddMinutes(-10+10*ratio);
            var sample=Samples.OrderBy(s => Math.Abs((s.At-time).TotalSeconds)).First();
            ToolTip=SampleText(sample);
            InvalidateVisual();
        }
        protected override void OnRender(DrawingContext dc) {
            base.OnRender(dc); double w=ActualWidth,h=ActualHeight; if(w<2||h<2)return;
            dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,w,h));
            if(IsKeyboardFocused)dc.DrawRectangle(null,new Pen(new SolidColorBrush(Color.FromRgb(125,219,224)),2),new Rect(1,1,w-2,h-2));
            double left=Spark?0:PlotLeft, top=Spark?2:14, bottom=Spark?h-2:h-19;
            double min,max;Range(Samples,out min,out max);double progress=(double)GetValue(ProgressProperty);
            var previous=progress<1?previousSamples:null;
            if(previous!=null){double oldMin,oldMax;Range(previous,out oldMin,out oldMax);min=oldMin+(min-oldMin)*progress;max=oldMax+(max-oldMax)*progress;}
            var gridPen=new Pen(new SolidColorBrush(Color.FromArgb(28,255,255,255)),1);
            if(!Spark) {
                dc.DrawLine(gridPen,new Point(left,top),new Point(left,bottom)); dc.DrawLine(gridPen,new Point(left,bottom),new Point(w-4,bottom));
                Label(dc,AxisLabel(max),0,0,"#A4A6AF"); Label(dc,AxisLabel(min),0,bottom-10,"#A4A6AF");
                Label(dc,"−10 min",left,bottom+5,"#A4A6AF"); Label(dc,"now",w-26,bottom+5,"#A4A6AF");
                if(!Cpu&&Threshold>0&&Threshold>=min&&Threshold<=max) { double y=bottom-(bottom-top)*(Threshold-min)/(max-min); var pen=new Pen(new SolidColorBrush(Color.FromArgb(110,255,178,36)),1); pen.DashStyle=new DashStyle(new double[]{2,3},0); dc.DrawLine(pen,new Point(left,y),new Point(w-4,y)); }
            }
            if(Samples.Count==0) { if(!Spark)Label(dc,"Collecting samples…",left+20,(top+bottom)/2,"#A4A6AF"); return; }
            var last=Samples.Last().At; var first=Spark?Samples.First().At:last.AddMinutes(-10); double seconds=Math.Max(1,(last-first).TotalSeconds);
            var points=Samples.Select(s=>{
                double x=Math.Max(0,Math.Min(1,(s.At-first).TotalSeconds/seconds)),value=Cpu?s.Cpu:s.Memory;
                if(previous!=null){
                    var old=previous.LastOrDefault(p=>p.At<=s.At)??previous.First();var oldLast=previous.Last().At;var oldFirst=Spark?previous.First().At:oldLast.AddMinutes(-10);
                    double oldX=Math.Max(0,Math.Min(1,(old.At-oldFirst).TotalSeconds/Math.Max(1,(oldLast-oldFirst).TotalSeconds))),oldValue=Cpu?old.Cpu:old.Memory;
                    x=oldX+(x-oldX)*progress;value=oldValue+(value-oldValue)*progress;
                }
                return new Point(left+x*(w-left-4),bottom-Math.Max(0,Math.Min(1,(value-min)/(max-min)))*(bottom-top));
            }).ToList();
            if(points.Count>1) {
                var line=new StreamGeometry(); using(var context=line.Open()) { context.BeginFigure(points[0],false,false); context.PolyLineTo(points.Skip(1).ToArray(),true,false); } line.Freeze();
                if(!Spark) { var fill=new StreamGeometry(); using(var c=fill.Open()) { c.BeginFigure(new Point(points[0].X,bottom),true,true); c.PolyLineTo(points,true,false); c.LineTo(new Point(points.Last().X,bottom),true,false); } fill.Freeze(); dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(32,Stroke.R,Stroke.G,Stroke.B),Color.FromArgb(0,Stroke.R,Stroke.G,Stroke.B),90),null,fill); }
                dc.DrawGeometry(null,new Pen(new SolidColorBrush(Stroke),Spark?1.15:1.5),line);
            }
            if(!Spark)dc.DrawEllipse(new SolidColorBrush(Stroke),null,points.Last(),2.5,2.5);
            if(IsKeyboardFocused&&!Spark&&selected>=0&&selected<Samples.Count){var point=points[selected];dc.DrawLine(new Pen(new SolidColorBrush(Stroke),1),new Point(point.X,top),new Point(point.X,bottom));Label(dc,SampleText(Samples[selected]),left+8,0,"#F5F5F7");}
            if(hover.HasValue)dc.DrawLine(gridPen,new Point(hover.Value,top),new Point(hover.Value,bottom));
        }
        string AxisLabel(double value){return Cpu?value.ToString("0.###",CultureInfo.InvariantCulture)+"%":(value/(value>=Format.GB?Format.GB:Format.MB)).ToString(value>=Format.GB?"0.00":"0.0",CultureInfo.InvariantCulture)+(value>=Format.GB?" GB":" MB");}
        void Label(DrawingContext dc,string value,double x,double y,string color) {
            var text=new FormattedText(new Strings(UiLanguage).T(value),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(UiLanguage=="ko"?"Malgun Gothic":"Consolas"),9,(Brush)new BrushConverter().ConvertFromString(color),1.0);
            dc.DrawText(text,new Point(Math.Max(0,x),Math.Max(0,y)));
        }
    }
}
