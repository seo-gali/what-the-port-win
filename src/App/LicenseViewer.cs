#if SINGLE_FILE
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;

namespace WhatThePort.App {
    public static class LicenseViewer {
        static string Read(string name){
            using(var stream=typeof(LicenseViewer).Assembly.GetManifestResourceStream(name))
            using(var reader=new StreamReader(stream))return reader.ReadToEnd();
        }
        public static int Show(){
            var app=new Application();
            var text=new TextBox{Text="What the Port for Windows\n\n"+Read("WhatThePort.LICENSE.txt")+"\n\nGeist fonts — SIL Open Font License\n\n"+Read("WhatThePort.OFL.txt"),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(18),FontSize=14,Background=new SolidColorBrush(Color.FromRgb(26,29,36)),Foreground=Brushes.White,BorderThickness=new Thickness(0)};
            var window=new Window{Title="What the Port - Licenses",Width=720,Height=600,MinWidth=320,MinHeight=240,WindowStartupLocation=WindowStartupLocation.CenterScreen,Content=text};
            AutomationProperties.SetName(window,window.Title);AutomationProperties.SetName(text,"License text");
            app.Run(window);return 0;
        }
    }
}
#endif