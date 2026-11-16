using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhatThePort;

// Targeted regressions, not a security certification or an antivirus scan.
public static class SecurityAudit {
    static int passed,failed;
    static void Check(string name,bool condition){Console.WriteLine((condition?"PASS ":"FAIL ")+name);if(condition)passed++;else failed++;}
    static string Folder(string root,string name){string path=Path.Combine(root,name);Directory.CreateDirectory(path);return path;}
    static void Head(string folder,string value){Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"HEAD"),value);}
    static void Metadata(string root){
        string project=Folder(root,"project"),git=Folder(project,".git");Folder(git,"objects");
        Head(git,"ref: refs/heads/main");Check("S1 regular repository branch remains available",ProjectMetadata.Branch(project)=="main");
        string nested=Folder(project,"src");Check("S1 nested project resolves its repository",ProjectMetadata.Branch(nested)=="main");
        Head(git,"ref: refs/heads/feature/\ud55c\uae00");Check("S1 Unicode branch remains available",ProjectMetadata.Branch(project)=="feature/\ud55c\uae00");
        Head(git,new String('a',40));Check("S1 detached SHA-1 remains available",ProjectMetadata.Branch(project)=="aaaaaaaa");
        Head(git,new String('b',64));Check("S1 detached SHA-256 remains available",ProjectMetadata.Branch(project)=="bbbbbbbb");
        Head(git,"not a HEAD");Check("S1 malformed metadata is omitted",ProjectMetadata.Branch(project)==null);
        Head(git,"ref: refs/heads/"+new String('a',4096));Check("S1 oversized metadata is omitted",ProjectMetadata.Branch(project)==null);
        Head(git,"ref: refs/heads/main");
        string outside=Folder(root,"outside-metadata"),untrusted=Folder(root,"untrusted-project");Head(outside,"ref: refs/heads/PUBLIC_MARKER_ONLY");Folder(outside,"objects");
        File.WriteAllText(Path.Combine(untrusted,".git"),"gitdir: "+outside);
        Check("S1 arbitrary external gitdir is not read",ProjectMetadata.Branch(untrusted)==null);
        Check("S1 UNC and device paths are rejected before filesystem I/O",!ProjectMetadata.IsLocalPath(@"\\audit.invalid\share\git")&&!ProjectMetadata.IsLocalPath(@"\\?\C:\git")&&!ProjectMetadata.IsLocalPath(@"\\.\C:\git")&&!ProjectMetadata.IsLocalPath("C:relative"));
        string worktree=Folder(root,"linked-worktree"),metadata=Folder(git,@"worktrees\linked");
        File.WriteAllText(Path.Combine(worktree,".git"),"gitdir: "+metadata);
        File.WriteAllText(Path.Combine(metadata,"gitdir"),Path.Combine(worktree,".git"));File.WriteAllText(Path.Combine(metadata,"commondir"),"../..");
        Head(metadata,"ref: refs/heads/worktree");Check("S1 verified local worktree remains available",ProjectMetadata.Branch(worktree)=="worktree");
        File.WriteAllText(Path.Combine(metadata,"gitdir"),Path.Combine(untrusted,".git"));Check("S1 wrong worktree backlink is rejected",ProjectMetadata.Branch(worktree)==null);
        File.WriteAllText(Path.Combine(metadata,"gitdir"),Path.Combine(worktree,".git"));
        string submodule=Folder(project,"sub"),subgit=Folder(git,@"modules\sub");Head(subgit,"ref: refs/heads/submodule");
        File.WriteAllText(Path.Combine(submodule,".git"),"gitdir: ../.git/modules/sub");Check("S1 ancestor repository submodule remains available",ProjectMetadata.Branch(submodule)=="submodule");
        string linkedSub=Folder(worktree,"sub");File.WriteAllText(Path.Combine(linkedSub,".git"),"gitdir: "+subgit);Check("S1 worktree submodule remains available",ProjectMetadata.Branch(linkedSub)=="submodule");
        string reparse=Path.Combine(root,"reparse-project");
        Check("S1 reparse components cannot redirect metadata",(File.GetAttributes(reparse)&FileAttributes.ReparsePoint)!=0&&!ProjectMetadata.IsLocalPath(reparse)&&ProjectMetadata.Branch(reparse)==null);
    }
    static void Terminal(string root){
        string project=Folder(root,"terminal-project"),original=Environment.CurrentDirectory;
        try{
            File.WriteAllText(Path.Combine(project,"wt.exe"),"untrusted marker, never executed");Environment.CurrentDirectory=project;
            var start=Launchers.TerminalStartInfo(project,"Windows Terminal",null);
            string alias=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"Microsoft\WindowsApps\wt.exe");
            string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
            Check("S2 Windows Terminal uses its absolute app execution alias",start.FileName==alias&&!start.UseShellExecute&&start.WorkingDirectory==project);
            Check("S2 nested PowerShell uses the system absolute path",start.Arguments.StartsWith("new-tab \""+powershell+"\" -NoLogo -NoExit -EncodedCommand ",StringComparison.Ordinal));
            Check("S2 direct PowerShell also uses the system absolute path",Launchers.TerminalStartInfo(project,"PowerShell",null).FileName==powershell);
        }finally{Environment.CurrentDirectory=original;}
    }
    static void ProcessPolicy(){
        DateTime now=DateTime.UtcNow;
        var processes=new Dictionary<int,ProcessNode>{
            {100,new ProcessNode{Pid=100,Name="node",Started=now.AddHours(-2),Memory=100,Cpu=1}},
            {200,new ProcessNode{Pid=200,Parent=100,Name="worker",Started=now.AddMinutes(-10),Memory=50,Cpu=.5}},
            {300,new ProcessNode{Pid=300,Parent=200,Name="old-orphan",Started=now.AddHours(-1)}},
            {400,new ProcessNode{Pid=400,Parent=300,Name="orphan-child",Started=now.AddMinutes(-1)}}};
        var tree=Policy.Tree(100,processes,new HashSet<int>{100});
        Check("S3 reused parent generation excludes the orphan and its descendants",tree.Select(n=>n.Pid).SequenceEqual(new[]{100,200}));
        Check("S3 valid measured tree retains CPU and memory",tree.Sum(n=>n.Memory)==150&&tree.Sum(n=>n.Cpu)==1.5);
        var idle=new Server{Started=now,SampledAt=now,Observed=now.AddHours(-5),LastActive=now.AddHours(-5)};var settings=new Settings();
        Check("S4 unknown or partial observation cannot be automatically stopped",!Policy.CleanupCandidate(idle,settings,now));
        idle.ObservationComplete=true;Check("S4 completely observed idle server remains eligible",Policy.CleanupCandidate(idle,settings,now));
        idle.HasEstablishedConnections=true;Check("S4 tree activity blocks cleanup even with zero inbound connections",idle.Connections==0&&!Policy.CleanupCandidate(idle,settings,now));
        string json=Store.Json(idle);Check("S4 internal observation state preserves the public JSON schema",!json.Contains("ObservationComplete")&&!json.Contains("HasEstablishedConnections"));
    }
    public static int Main(string[] args){
        try{
            string root=Path.GetFullPath(args[0]);Metadata(root);Terminal(root);ProcessPolicy();
            string storage=Folder(root,"isolated-store");File.WriteAllText(Path.Combine(storage,"links.json"),"[{\"Port\":3000,\"Folder\":null}]");var store=new Store(storage);
            Check("S5 invalid link folder is ignored and lookup remains safe",store.LinkFor(new Server{Port=3000,Folder=root})==null&&store.LastError!=null);
        }catch(Exception e){failed++;Console.WriteLine("FAIL unexpected: "+e);}
        Console.WriteLine(passed+" security regressions passed, "+failed+" failed. No user process stopped or remote request made.");return failed==0?0:1;
    }
}