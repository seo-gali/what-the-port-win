using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace WhatThePort {
    public static class Native {
        [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll")] static extern int GetCurrentProcessId();
        [DllImport("psapi.dll", SetLastError=true)] static extern bool GetProcessMemoryInfo(IntPtr process,ref ProcessMemory memory,uint size);
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool TerminateProcess(IntPtr handle, uint code);
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool IsProcessCritical(IntPtr handle,out bool critical);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true, ExactSpelling=true)] static extern bool QueryFullProcessImageNameW(IntPtr handle,uint flags,StringBuilder name,ref uint size);
        [DllImport("kernel32.dll", SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
        [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(int pid, out int session);
        [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snapshot, ref Entry entry);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool Process32NextW(IntPtr snapshot, ref Entry entry);
        [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr handle, int information, IntPtr data, int size, out int returned);
        [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] buffer, int length, out IntPtr read);
        [DllImport("kernel32.dll")] static extern bool IsWow64Process(IntPtr process, out bool wow64);
        [DllImport("kernel32.dll", SetLastError=true)] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct Entry {
            public uint Size, Usage, Pid; public IntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string Name;
        }
        [StructLayout(LayoutKind.Sequential)] struct MemoryStatus {
            public uint Length, Load; public ulong Total, Available, PageTotal, PageAvailable, VirtualTotal, VirtualAvailable, Extended;
        }
        [StructLayout(LayoutKind.Sequential)] struct ProcessMemory {
            public uint Size,PageFaults;
            public UIntPtr PeakWorkingSet,WorkingSet,PeakPagedPool,PagedPool,PeakNonPagedPool,NonPagedPool,Pagefile,PeakPagefile,PrivateUsage;
        }
        static readonly string CurrentSid = ReadCurrentSid();
        static readonly int CurrentSession = ReadCurrentSession();
        static string ReadCurrentSid(){using(var identity=WindowsIdentity.GetCurrent())return identity.User.Value;}
        static int ReadCurrentSession(){int session;if(!ProcessIdToSessionId(GetCurrentProcessId(),out session))throw new Win32Exception();return session;}

        // Read metrics from one query-only handle. Process.WorkingSet64 builds
        // system-wide process information for every sampled process on Framework.
        public static bool SampleProcess(int pid,out DateTime started,out double cpuMilliseconds,out long workingSet) {
            started=default(DateTime);cpuMilliseconds=0;workingSet=0;
            IntPtr process=OpenProcess(0x1000,false,pid);if(process==IntPtr.Zero)return false;
            try {
                long created,exited,kernel,user;
                if(!OwnedByCurrentUser(process,pid)||!GetProcessTimes(process,out created,out exited,out kernel,out user))return false;
                var memory=new ProcessMemory{Size=(uint)Marshal.SizeOf(typeof(ProcessMemory))};
                if(!GetProcessMemoryInfo(process,ref memory,memory.Size))return false;
                started=DateTime.FromFileTimeUtc(created);cpuMilliseconds=((double)kernel+user)/TimeSpan.TicksPerMillisecond;workingSet=(long)memory.WorkingSet.ToUInt64();
                return true;
            } finally {CloseHandle(process);}
        }

        public static Dictionary<int, ProcessNode> Processes() {
            var result = new Dictionary<int, ProcessNode>();
            IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot == new IntPtr(-1)) throw new Win32Exception();
            try {
                Entry e = new Entry(); e.Size = (uint)Marshal.SizeOf(typeof(Entry));
                if (!Process32FirstW(snapshot, ref e)) throw new Win32Exception();
                do { result[(int)e.Pid] = new ProcessNode { Pid=(int)e.Pid, Parent=(int)e.Parent, Name=Path.GetFileNameWithoutExtension(e.Name).ToLowerInvariant() }; } while (Process32NextW(snapshot, ref e));
            } finally { CloseHandle(snapshot); }
            return result;
        }
        public static List<SocketRow> Sockets() {
            var result = new List<SocketRow>();
            foreach (int family in new [] {2, 23}) {
                int size = 0;
                uint status = GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 5, 0);
                if (status == 50 && family == 23) continue;
                if (status != 122 && status != 0) throw new Win32Exception((int)status);
                bool succeeded = false;
                for (int attempt=0; attempt<4; attempt++) {
                    IntPtr memory = Marshal.AllocHGlobal(Math.Max(size, 4));
                    try {
                        status = GetExtendedTcpTable(memory, ref size, false, family, 5, 0);
                        if (status == 122) continue;
                        if (status != 0) throw new Win32Exception((int)status);
                        int count = Marshal.ReadInt32(memory), stride = family == 2 ? 24 : 56;
                        if (count < 0 || 4L + count * (long)stride > size) throw new InvalidDataException("Invalid TCP table.");
                        for (int i=0; i<count; i++) {
                            IntPtr row = IntPtr.Add(memory, 4 + i*stride);
                            int portOffset = family == 2 ? 8 : 20;
                            int port = Marshal.ReadByte(row, portOffset)*256 + Marshal.ReadByte(row, portOffset+1);
                            byte[] address = new byte[family == 2 ? 4 : 16];
                            Marshal.Copy(IntPtr.Add(row, family == 2 ? 4 : 0), address, 0, address.Length);
                            byte[] remote=new byte[address.Length];
                            Marshal.Copy(IntPtr.Add(row,family==2?12:24),remote,0,remote.Length);
                            int remotePortOffset=family==2?16:44;
                            result.Add(new SocketRow { Pid=Marshal.ReadInt32(row, family == 2 ? 20 : 52), State=Marshal.ReadInt32(row, family == 2 ? 0 : 48), Port=port,
                                Address=family==2?new System.Net.IPAddress(address).ToString():new System.Net.IPAddress(address,(uint)System.Net.IPAddress.NetworkToHostOrder(Marshal.ReadInt32(row,16))).ToString(),
                                RemoteAddress=family==2?new System.Net.IPAddress(remote).ToString():new System.Net.IPAddress(remote,(uint)System.Net.IPAddress.NetworkToHostOrder(Marshal.ReadInt32(row,40))).ToString(),
                                RemotePort=Marshal.ReadByte(row,remotePortOffset)*256+Marshal.ReadByte(row,remotePortOffset+1) });
                        }
                        succeeded = true; break;
                    } finally { Marshal.FreeHGlobal(memory); }
                }
                if (!succeeded) throw new IOException("TCP table changed too quickly. Retrying next scan.");
            }
            return result;
        }
        public static void Memory(out long total, out long free) {
            MemoryStatus s = new MemoryStatus(); s.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            if (!GlobalMemoryStatusEx(ref s)) throw new Win32Exception();
            total=(long)s.Total; free=(long)s.Available;
        }
        public static bool OwnedByCurrentUser(IntPtr handle, int pid) {
            int session; if (!ProcessIdToSessionId(pid, out session) || session != CurrentSession) return false;
            IntPtr token; if (!OpenProcessToken(handle, 8, out token)) return false;
            try { using (var identity = new WindowsIdentity(token)) { return identity.User != null && identity.User.Value == CurrentSid; } }
            finally { CloseHandle(token); }
        }
        public static bool IsOwned(int pid) {
            IntPtr h = OpenProcess(0x1000, false, pid); if (h == IntPtr.Zero) return false;
            try { return OwnedByCurrentUser(h, pid); } finally { CloseHandle(h); }
        }
        public static bool SameStart(DateTime actual, DateTime expected) { return actual.ToUniversalTime().Ticks == expected.ToUniversalTime().Ticks; }
        public static bool Stop(ProcessNode node, out string error) {
            return Stop(node,out error,false);
        }
        public static bool ForceStop(ProcessNode node,out string error) {
            return Stop(node,out error,true);
        }
        static bool Stop(ProcessNode node,out string error,bool force) {
            error = null;
            if (node.Pid <= 4 || (force?Policy.IsCriticalName(node.Name):Policy.IsProtectedName(node.Name)) || Policy.Ancestors(Process.GetCurrentProcess().Id, Processes()).Contains(node.Pid)) { error="Protected process."; return false; }
            // A single handle pins the identity through validation and termination.
            IntPtr h = OpenProcess(0x101001, false, node.Pid);
            if (h == IntPtr.Zero) { error="Process exited or access denied."; return false; }
            try {
                long created, exited, kernel, user;
                if (!GetProcessTimes(h, out created, out exited, out kernel, out user) || !SameStart(DateTime.FromFileTimeUtc(created), node.Started)) { error="Process identity changed; stop cancelled."; return false; }
                if (!OwnedByCurrentUser(h, node.Pid)) { error="Process belongs to another user or session."; return false; }
                var image=new StringBuilder(32768);uint length=(uint)image.Capacity;bool critical;
                if(!QueryFullProcessImageNameW(h,0,image,ref length)||!IsProcessCritical(h,out critical)){error="Process identity unavailable; stop cancelled.";return false;}
                string actualName=Path.GetFileNameWithoutExtension(image.ToString());
                if(critical||(force?Policy.IsCriticalName(actualName):Policy.IsProtectedName(actualName))){error="Protected process.";return false;}
                if (!TerminateProcess(h, 0)) { error=new Win32Exception().Message; return false; }
                uint wait=WaitForSingleObject(h,2000);
                if(wait!=0){error=wait==258?"Process exit confirmation timed out.":new Win32Exception().Message;return false;}
                return true;
            } finally { CloseHandle(h); }
        }
        public static string CurrentDirectory(int pid) {
            // Read ONLY the current-directory UNICODE_STRING, never environment or command-line buffers.
            IntPtr h = OpenProcess(0x410, false, pid); if (h == IntPtr.Zero) return null;
            try {
                if (!OwnedByCurrentUser(h, pid)) return null;
                bool wow; if (!IsWow64Process(h, out wow)) return null;
                IntPtr peb;
                IntPtr info = Marshal.AllocHGlobal(48);
                try {
                    int returned;
                    if (NtQueryInformationProcess(h, wow ? 26 : 0, info, wow ? 8 : 48, out returned) != 0) return null;
                    peb = Marshal.ReadIntPtr(info, wow ? 0 : 8);
                } finally { Marshal.FreeHGlobal(info); }
                byte[] pointer = Read(h, IntPtr.Add(peb, wow ? 0x10 : 0x20), wow ? 4 : 8);
                if (pointer == null) return null;
                IntPtr parameters = new IntPtr(wow ? BitConverter.ToUInt32(pointer, 0) : BitConverter.ToInt64(pointer, 0));
                byte[] unicode = Read(h, IntPtr.Add(parameters, wow ? 0x24 : 0x38), wow ? 8 : 16);
                if (unicode == null) return null;
                int length = BitConverter.ToUInt16(unicode, 0);
                if (length < 2 || length > 32766 || length % 2 != 0) return null;
                IntPtr text = new IntPtr(wow ? BitConverter.ToUInt32(unicode, 4) : BitConverter.ToInt64(unicode, 8));
                byte[] bytes = Read(h, text, length);
                return bytes == null ? null : Encoding.Unicode.GetString(bytes);
            } catch { return null; } finally { CloseHandle(h); }
        }
        static byte[] Read(IntPtr h, IntPtr address, int length) {
            byte[] buffer = new byte[length]; IntPtr read;
            return ReadProcessMemory(h, address, buffer, length, out read) && read.ToInt64() == length ? buffer : null;
        }
        public static void SignalExisting() {
            foreach (var p in Process.GetProcessesByName("WhatThePort")) {
                using (p) { try { if (p.Id != Process.GetCurrentProcess().Id && p.SessionId == CurrentSession) PostMessage(p.MainWindowHandle, 0x8001, IntPtr.Zero, IntPtr.Zero); } catch { } }
            }
        }
    }
}
