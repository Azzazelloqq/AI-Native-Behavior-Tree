const http = require('http');
const fs = require('fs');
const path = require('path');
const root = path.resolve(process.argv[2]);
const port = Number(process.argv[3]);
const types = {'.html':'text/html','.js':'application/javascript','.wasm':'application/wasm','.data':'application/octet-stream','.json':'application/json'};
http.createServer((req,res) => {
  let relative = decodeURIComponent(req.url.split('?')[0]);
  if (relative === '/') relative = '/index.html';
  const file = path.resolve(root, '.' + relative);
  if (!file.startsWith(root + path.sep)) { res.writeHead(403); return res.end(); }
  fs.readFile(file,(error,data) => {
    if (error) { res.writeHead(404); return res.end(); }
    res.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
    res.setHeader('Cross-Origin-Opener-Policy','same-origin');
    res.setHeader('Cross-Origin-Embedder-Policy','require-corp');
    res.writeHead(200); res.end(data);
  });
}).listen(port,'127.0.0.1',() => console.log('AIBT_WEB_SERVER_READY|' + port));
