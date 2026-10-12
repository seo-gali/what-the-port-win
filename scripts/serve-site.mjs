import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../docs');
const prefix = '/what-the-port-win';
const port = Number(process.argv[2] || 4173);
const types = {'.html':'text/html; charset=utf-8','.css':'text/css; charset=utf-8','.js':'text/javascript; charset=utf-8','.svg':'image/svg+xml','.png':'image/png','.jpg':'image/jpeg','.ttf':'font/ttf','.txt':'text/plain; charset=utf-8','.zip':'application/zip','.json':'application/json; charset=utf-8'};
http.createServer((req,res) => {
  const headers = {'X-Content-Type-Options':'nosniff','Referrer-Policy':'no-referrer','X-Frame-Options':'DENY','Content-Security-Policy':"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",'Cache-Control':'no-cache'};
  if (req.method !== 'GET' && req.method !== 'HEAD') {res.writeHead(405, {...headers,Allow:'GET, HEAD'});res.end();return;}
  let requested;
  try { requested = decodeURIComponent(new URL(req.url,'http://localhost').pathname); } catch {res.writeHead(400,headers);res.end();return;}
  if (requested === prefix) {res.writeHead(302,{...headers,Location:prefix+'/'});res.end();return;}
  if (requested.startsWith(prefix+'/')) requested = requested.slice(prefix.length);
  const target = path.resolve(root, '.' + (requested === '/' ? '/index.html' : requested));
  if (!target.startsWith(root + path.sep) || requested.includes('\\') || requested.includes('\0')) {res.writeHead(403,headers);res.end();return;}
  let file=target,status=200;
  try {if (!fs.statSync(file).isFile()) throw new Error('Not a file');} catch {file=path.join(root,'404.html');status=404;}
  const extension=path.extname(file).toLowerCase();
  if (extension === '.zip') headers['Content-Disposition']='attachment; filename="WhatThePort-Windows-x64.zip"';
  res.writeHead(status,{...headers,'Content-Type':types[extension]||'application/octet-stream','Content-Length':fs.statSync(file).size});
  if (req.method === 'HEAD') {res.end();return;}
  const stream=fs.createReadStream(file);stream.on('error',()=>res.destroy());stream.pipe(res);
}).listen(port,'127.0.0.1',()=>console.log(`Local: http://127.0.0.1:${port}`));
