using System;
using System.IO;
using System.Linq;

namespace WhatThePort {
    public static class ProjectMetadata {
        public static bool IsLocalPath(string path) {
            try {
                if(String.IsNullOrEmpty(path)||path.Length<3||!Char.IsLetter(path[0])||path[1]!=':'||(path[2]!='\\'&&path[2]!='/'))return false;
                string full=Path.GetFullPath(path),root=Path.GetPathRoot(full);
                var type=new DriveInfo(root).DriveType;if(type==DriveType.Network||type==DriveType.Unknown||type==DriveType.NoRootDirectory)return false;
                string current=root;
                foreach(string part in full.Substring(root.Length).Split(new[]{'\\','/'},StringSplitOptions.RemoveEmptyEntries)){
                    current=Path.Combine(current,part);if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)return false;
                }
                return true;
            }catch{return false;}
        }
        static string Read(string path) {
            if(!IsLocalPath(path))throw new IOException("Git metadata path is not a local regular path.");
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(file)){
                char[] text=new char[4097];int length=reader.ReadBlock(text,0,text.Length);if(length>4096)throw new InvalidDataException();return new string(text,0,length).Trim();
            }
        }
        static string Resolve(string folder,string value){return Path.GetFullPath(Path.Combine(folder,value));}
        static bool Same(string first,string second){return String.Equals(Path.GetFullPath(first).TrimEnd('\\'),Path.GetFullPath(second).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);}
        static bool GitDirectory(string path){return IsLocalPath(path)&&Directory.Exists(path)&&IsLocalPath(Path.Combine(path,"objects"))&&Directory.Exists(Path.Combine(path,"objects"));}
        static string Pointer(string marker,string folder){string text=Read(marker);return text.StartsWith("gitdir: ",StringComparison.Ordinal)?Resolve(folder,text.Substring(8)):null;}
        static string WorktreeCommon(string folder,string target) {
            try {
                if(!IsLocalPath(target)||!Directory.Exists(target))return null;
                var directory=new DirectoryInfo(target);if(directory.Parent==null||!String.Equals(directory.Parent.Name,"worktrees",StringComparison.OrdinalIgnoreCase)||directory.Parent.Parent==null)return null;
                string common=directory.Parent.Parent.FullName;if(!GitDirectory(common))return null;
                if(!Same(Resolve(target,Read(Path.Combine(target,"gitdir"))),Path.Combine(folder,".git"))||!Same(Resolve(target,Read(Path.Combine(target,"commondir"))),common))return null;
                return common;
            }catch{return null;}
        }
        static bool InModules(string target,string git){return target.StartsWith(Path.Combine(git,"modules")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        static bool AllowedPointer(string folder,string target) {
            if(!IsLocalPath(target)||!Directory.Exists(target))return false;
            if(WorktreeCommon(folder,target)!=null)return true;
            var parent=Directory.GetParent(folder);
            for(int i=0;parent!=null&&i<12;i++,parent=parent.Parent){
                string marker=Path.Combine(parent.FullName,".git");if(!IsLocalPath(marker))continue;
                if(Directory.Exists(marker)){if(GitDirectory(marker)&&InModules(target,marker))return true;}
                else {
                    string linked=Pointer(marker,parent.FullName);if(linked==null)continue;
                    string common=WorktreeCommon(parent.FullName,linked);
                    if(common!=null&&(InModules(target,linked)||InModules(target,common)))return true;
                }
            }
            return false;
        }
        public static string Branch(string folder) {
            try {
                if(!IsLocalPath(folder))return null;var directory=new DirectoryInfo(folder);
                for(int i=0;directory!=null&&i<12;i++,directory=directory.Parent){
                    if(!IsLocalPath(directory.FullName))return null;
                    string marker=Path.Combine(directory.FullName,".git");FileAttributes attributes;
                    try {attributes=File.GetAttributes(marker);}catch(FileNotFoundException){continue;}catch(DirectoryNotFoundException){continue;}
                    if((attributes&FileAttributes.ReparsePoint)!=0)return null;
                    string metadata=marker;
                    if((attributes&FileAttributes.Directory)==0){metadata=Pointer(marker,directory.FullName);if(metadata==null||!AllowedPointer(directory.FullName,metadata))return null;}
                    string head=Read(Path.Combine(metadata,"HEAD"));const string prefix="ref: refs/heads/";
                    if(head.StartsWith(prefix,StringComparison.Ordinal)){
                        string branch=head.Substring(prefix.Length);return branch.Length>0&&!branch.Any(c=>Char.IsControl(c)||c==' ')?branch:null;
                    }
                    return (head.Length==40||head.Length==64)&&head.All(Uri.IsHexDigit)?head.Substring(0,8):null;
                }
            }catch{}
            return null;
        }
    }
}
