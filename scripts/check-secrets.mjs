import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import assert from 'node:assert/strict';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const git=(args,input,encoding='utf8')=>{try{return execFileSync('git',['-c','core.quotepath=false',...args],{cwd:root,input,encoding,maxBuffer:128*1024*1024,windowsHide:true});}catch{throw new Error('Git inspection failed: '+args[0]);}};
const rules=[
 ['github-token',/\b(?:gh[pousr]_[A-Za-z0-9]{36,255}|github_pat_[A-Za-z0-9_]{60,255})\b/g],
 ['aws-access-key',/\b(?:AKIA|ASIA)[A-Z0-9]{16}\b/g],
 ['google-api-key',/\bAIza[A-Za-z0-9_-]{35}\b/g],
 ['service-secret-key',/\b(?:sk|rk)_(?:live|test)_[A-Za-z0-9]{16,}\b|\bsk-(?:proj-)?[A-Za-z0-9_-]{32,}\b/g],
 ['private-key',/-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----/g],
 ['credential-assignment',/\b(?:api[_-]?key|secret|password|passwd|access[_-]?token)\b\s*[:=]\s*["']([^"'\r\n]{8,})["']/gi]
];
const sensitivePublicFile=name=>name.startsWith('docs/')&&/(?:^|\/)(?:\.env(?:\.|$)|\.git(?:\/|$)|id_(?:rsa|ed25519)(?:$|\.)|(?:settings|links|credentials)\.json$)|\.(?:pfx|p12|key|pem)$/i.test(name);
function detect(text){
 const found=[];
 for(const [rule,pattern] of rules){
  pattern.lastIndex=0;
  for(const match of text.matchAll(pattern)){
   if(rule==='credential-assignment'&&/^(?:your[_ -]|example|sample|dummy|placeholder|changeme|replace|<|\$\{|\$env:)/i.test(match[1]))continue;
   found.push({rule,line:1+text.slice(0,match.index).split('\n').length-1});
  }
 }
 return found;
}
if(process.argv.includes('--self-test')){
 const cases=[
  ['github-token','gh'+'p_'+'A'.repeat(36)],
  ['aws-access-key','AK'+'IA'+'Z'.repeat(16)],
  ['google-api-key','AI'+'za'+'A'.repeat(35)],
  ['service-secret-key','sk'+'-proj-'+'A'.repeat(40)],
  ['private-key','-----BEGIN '+'PRIVATE KEY-----'],
  ['credential-assignment',"password='"+'un'+'safe-test-value'+"'"]
 ];
 for(const [rule,text] of cases)assert(detect(text).some(x=>x.rule===rule),'Missing detector '+rule);
 assert.equal(detect("password='YOUR_PASSWORD'").length,0);
 assert(sensitivePublicFile('docs/server.pfx'));assert(sensitivePublicFile('docs/.env'));assert(!sensitivePublicFile('docs/downloads/EXE-INFO.json'));
 console.log('PASS 10 secret-detector self-checks; values never printed');
 process.exit(0);
}
const findings=[],refs=new Set();let binarySkipped=0,largeSkipped=0,textScanned=0;
const binary=/\.(?:exe|dll|zip|png|jpe?g|webp|gif|ico|ttf|woff2?|pdf|pfx|p12)$/i;
function scan(bytes,location,collectRefs=false){
 if(bytes.length>2*1024*1024){largeSkipped++;return;}
 let text=bytes[0]===255&&bytes[1]===254?bytes.subarray(2).toString('utf16le'):bytes.toString('utf8');
 if(text.includes('\0')){binarySkipped++;return;}textScanned++;
 for(const hit of detect(text))findings.push({...location,...hit});
 if(collectRefs)for(const m of text.matchAll(/\$env:([A-Za-z_][A-Za-z0-9_]*)|process\.env\.([A-Za-z_][A-Za-z0-9_]*)|GetEnvironmentVariable\("([A-Za-z_][A-Za-z0-9_]*)"/g))refs.add(m[1]||m[2]||m[3]);
}
const working=[...new Set(git(['ls-files','-co','--exclude-standard','-z']).split('\0').filter(Boolean))];
let workingText=0;
for(const name of working){
 const full=path.resolve(root,name);if(!full.startsWith(root+path.sep)||!fs.existsSync(full)||fs.lstatSync(full).isSymbolicLink())continue;
 if(sensitivePublicFile(name))findings.push({scope:'public-file',path:name,rule:'sensitive-public-filename',line:0});
 if(binary.test(name)){binarySkipped++;continue;}
 if(fs.statSync(full).size>2*1024*1024){largeSkipped++;continue;}
 const before=textScanned;scan(fs.readFileSync(full),{scope:'working-tree',path:name},true);workingText+=textScanned-before;
}
const objects=new Map();
for(const line of git(['rev-list','--objects','--all']).trim().split('\n')){
 const space=line.indexOf(' ');if(space>0)objects.set(line.slice(0,space),line.slice(space+1));
}
const checked=git(['cat-file','--batch-check=%(objectname) %(objecttype) %(objectsize)'],[...objects.keys()].join('\n')+'\n');
const selected=[];
for(const line of checked.trim().split('\n')){
 const [id,type,size]=line.split(' ');if(type!=='blob')continue;
 if(/\.(?:pfx|p12)$/i.test(objects.get(id)))findings.push({scope:'git-history',object:id,path:objects.get(id),rule:'private-certificate-container',line:0});
 if(binary.test(objects.get(id))){binarySkipped++;continue;}
 if(Number(size)>2*1024*1024){largeSkipped++;continue;}selected.push(id);
}
if(selected.length){
 const batch=git(['cat-file','--batch'],selected.join('\n')+'\n',null);let at=0;
 for(const id of selected){
  const end=batch.indexOf(10,at);assert(end>=0,'Incomplete Git blob header');
  const [actual,type,length]=batch.subarray(at,end).toString('ascii').split(' ');assert.equal(actual,id);assert.equal(type,'blob');
  const start=end+1,finish=start+Number(length);assert(finish<=batch.length,'Incomplete Git blob');
  scan(batch.subarray(start,finish),{scope:'git-history',object:id,path:objects.get(id)});at=finish+1;
 }
}
const report={
 checkedAtUtc:new Date().toISOString(),head:git(['rev-parse','HEAD']).trim(),commits:Number(git(['rev-list','--count','--all']).trim()),
 workingTreeTextFiles:workingText,historyTextBlobs:textScanned-workingText,binarySkipped,largeSkipped,environmentVariableReferences:[...refs].sort(),findings,
 limits:'Pattern-based scan of reachable Git text blobs and nonignored working files; binaries and text over 2 MiB excluded. Live environment values, ignored secret files, remote provider validity and key rotation not accessed.'
};
fs.mkdirSync(path.join(root,'artifacts'),{recursive:true});
fs.writeFileSync(path.join(root,'artifacts/secrets-check.json'),JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({...report,findings:findings.map(x=>({...x,redacted:true}))},null,2));
process.exitCode=findings.length?1:0;