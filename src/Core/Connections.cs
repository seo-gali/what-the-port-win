using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace WhatThePort {
    public sealed class Connections {
        readonly List<SocketRow> established;
        readonly ILookup<string,SocketRow> endpoints;
        public Connections(IEnumerable<SocketRow> sockets){
            established=sockets.Where(s=>s.State==5).ToList();
            endpoints=established.ToLookup(s=>Key(s.Address,s.Port,s.RemoteAddress,s.RemotePort));
        }
        static string Address(string value){
            IPAddress ip;if(!IPAddress.TryParse(value,out ip))return value;
            return (ip.IsIPv4MappedToIPv6?ip.MapToIPv4():ip).ToString();
        }
        static string Key(string local,int port,string remote,int remotePort){return Address(local)+"|"+port+"|"+Address(remote)+"|"+remotePort;}
        public Dictionary<int,int> ClientPids(Server server){
            var clients=new Dictionary<int,int>();if(server.Processes.Count==0)return clients;int root=server.Processes[0].Pid;
            foreach(var inbound in established.Where(s=>s.Pid==root&&server.Ports.Contains(s.Port))){
                foreach(int pid in endpoints[Key(inbound.RemoteAddress,inbound.RemotePort,inbound.Address,inbound.Port)].Select(s=>s.Pid).Distinct()){
                    int count;clients.TryGetValue(pid,out count);clients[pid]=count+1;
                }
            }
            return clients;
        }
    }
}
