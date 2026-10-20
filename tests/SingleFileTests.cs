using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Security.Cryptography;
using WhatThePort.App;

public static class SingleFileTests {
    static void Check(bool result,string name){if(!result)throw new Exception(name);Console.WriteLine("PASS standalone: "+name);}
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    [STAThread] public static int Main(string[] args){
        try{
            var assembly=typeof(Panel).Assembly;
            Check(!assembly.GetReferencedAssemblies().Any(a=>a.Name=="WhatThePort.Core"),"no external Core DLL reference");
            string[] expected=args[1].Split(',');int index=0;
            foreach(string name in new[]{"LICENSE","OFL"})using(var stream=assembly.GetManifestResourceStream("WhatThePort."+name+".txt"))using(var bytes=new MemoryStream()){
                stream.CopyTo(bytes);Check(Hash(bytes.ToArray())==expected[index++],"complete embedded "+name);
            }
            for(int version=1;version<=4;version++)AppContext.SetSwitch("Switch.UseLegacyAccessibilityFeatures"+(version==1?"":"."+version),false);
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var panel=new Panel(true,true,null,"en");
            foreach(string family in new[]{"Geist","Geist Mono"})foreach(var weight in new[]{FontWeights.Normal,FontWeights.Medium}){
                var font=new FontFamily(new Uri("pack://application:,,,/WhatThePort;component/fonts/"),"./#"+family);
                GlyphTypeface glyph;bool found=new Typeface(font,FontStyles.Normal,weight,FontStretches.Normal).TryGetGlyphTypeface(out glyph);
                Check(found&&glyph.FontUri.Scheme=="pack"&&glyph.FamilyNames.Values.Any(n=>n==family),family+" "+weight+" resolves embedded font without fallback");
            }
            foreach(string name in new[]{"Geist-Regular.ttf","Geist-Medium.ttf","GeistMono-Regular.ttf","GeistMono-Medium.ttf"}){
                var resource=Application.GetResourceStream(new Uri("pack://application:,,,/WhatThePort;component/fonts/"+name.ToLowerInvariant()));
                using(var stream=resource.Stream)using(var bytes=new MemoryStream()){
                    stream.CopyTo(bytes);Check(Hash(bytes.ToArray())==expected[index++],"embedded font bytes match "+name);
                }
            }
            panel.Window.Measure(new Size(440,594));panel.Window.Arrange(new Rect(0,0,440,594));panel.Window.UpdateLayout();
            Check(panel.Window.FontFamily.Source=="./#Geist"&&panel.Window.FindName("Body")!=null,"standalone WPF panel loads embedded markup");
            panel.Quit();return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
